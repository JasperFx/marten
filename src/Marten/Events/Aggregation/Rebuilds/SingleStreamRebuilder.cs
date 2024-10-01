using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Internals;
using Marten.Events.Daemon.Progress;
using Marten.Internal.Sessions;
using Marten.Linq.QueryHandlers;
using Marten.Services;
using Marten.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Marten.Events.Aggregation.Rebuilds;

public class SingleStreamRebuilder<TDoc, TId>: IReplayExecutor
{
    public const string Backfill = "Backfill";
    private readonly IMartenDatabase _database;
    private readonly IAggregationRuntime<TDoc, TId> _runtime;
    private readonly DocumentStore _store;
    private long _ceiling;
    private readonly ILogger _logger;

    public SingleStreamRebuilder(DocumentStore store, IMartenDatabase database,
        IAggregationRuntime<TDoc, TId> runtime)
    {
        _store = store;
        _database = database;
        _runtime = runtime;
        _logger = store.Options.LogFactory?.CreateLogger<SingleStreamRebuilder<TDoc, TId>>() ?? store.Options.DotNetLogger ?? NullLogger<SingleStreamRebuilder<TDoc, TId>>.Instance;
    }

    public Task StartAsync(SubscriptionExecutionRequest request, ISubscriptionController controller,
        CancellationToken cancellation)
    {
        return RebuildAllAsync(request.Runtime, controller.Name, _runtime.Projection, cancellation);
    }

    public async Task RebuildAllAsync(IDaemonRuntime runtime, ShardName shardName, IAggregateProjection projection,
        CancellationToken token)
    {
        _ceiling = runtime.HighWaterMark();

        await using var session = await initializeAsync(runtime, projection, shardName, projection.Options, token).ConfigureAwait(false);

        if (_store.Options.EventGraph.TenancyStyle == TenancyStyle.Conjoined)
        {
            var tenantIds = await session.ExecuteHandlerAsync(new QueryForTenantIds(_store.Options, typeof(TDoc)), token).ConfigureAwait(false);

            // TODO -- could parallelize this maybe?
            foreach (var tenantId in tenantIds)
            {
                _logger.LogInformation("Starting optimized rebuild for {ShardName} at tenant {TenantId}", shardName.Identity, tenantId);
                await processSpecificTenant(runtime, shardName, token, tenantId).ConfigureAwait(false);
                _logger.LogInformation("Finished optimized rebuild for {ShardName} at tenant {TenantId}", shardName.Identity, tenantId);
            }
        }
        else
        {
            // Single tenancy, just go
            await processSingleTenant(runtime, shardName, token, session).ConfigureAwait(false);
        }

        // Mark as running continuously -- come back to this!
        session.QueueOperation(new MarkShardModeAsContinuous(shardName, _store.Options.EventGraph, _ceiling));
        await session.SaveChangesAsync(token).ConfigureAwait(false);
    }

    private async Task processSpecificTenant(IDaemonRuntime runtime, ShardName shardName, CancellationToken token,
        string tenantId)
    {
        var options = SessionOptions.ForDatabase(tenantId, _database);

        await using var tenantSession = (DocumentSessionBase)_store.LightweightSession(options);
        await processSingleTenant(runtime, shardName, token, tenantSession).ConfigureAwait(false);
    }

    private async Task processSingleTenant(IDaemonRuntime runtime, ShardName shardName, CancellationToken token,
        DocumentSessionBase session)
    {
        while (!token.IsCancellationRequested)
        {
            var shouldContinue = await executeNextBatch(runtime, shardName, token, session).ConfigureAwait(false);
            if (!shouldContinue)
            {
                break;
            }
        }
    }

    private async Task<bool> executeNextBatch(IDaemonRuntime runtime, ShardName shardName, CancellationToken token,
        DocumentSessionBase session)
    {
        // QueryForNextAggregateIds accounts for tenancy by pulling it from the session
        var ids = await session
            .ExecuteHandlerAsync(new QueryForNextAggregateIds(_store.Options, typeof(TDoc)), token)
            .ConfigureAwait(false);

        if (!ids.Any())
        {
            return false;
        }

        var pageHandler = new AggregatePageHandler<TDoc, TId>(_ceiling, _store, session, _runtime, ids);
        await pageHandler.ProcessPageAsync(runtime, shardName, _store.Options.Projections.RebuildErrors, token)
            .ConfigureAwait(false);

        return true;
    }

    private async Task<DocumentSessionBase> initializeAsync(IDaemonRuntime runtime, IAggregateProjection projection,
        ShardName shardName,
        AsyncOptions asyncOptions,
        CancellationToken token)
    {
        var options = SessionOptions.ForDatabase(_database);
        options.AllowAnyTenant = true;

        // *This* session's lifecycle will be managed outside of this method
        var session = (DocumentSessionBase)_store.LightweightSession(options);
        try
        {
            await _database.EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

            // TODO -- what if you have two single stream projections for one aggregate?
            await backfillStreamTypeAliases(runtime.HighWaterMark(), projection, token).ConfigureAwait(false);

            await _database.EnsureStorageExistsAsync(typeof(TDoc), token).ConfigureAwait(false);

            var state = await tryFindExistingState(shardName, session, token).ConfigureAwait(false);
            if (state == null || state.Mode != ShardMode.rebuilding)
            {
                asyncOptions.Teardown(session);
                session.QueueOperation(new SeedAggregateRebuildTable(_store.Options, typeof(TDoc)));
                session.QueueOperation(new MarkShardModeAsRebuilding(shardName, _store.Events, _ceiling));
                await session.SaveChangesAsync(token).ConfigureAwait(false);
            }
            else
            {
                _ceiling = state.RebuildThreshold;
            }

            return session;
        }
        catch
        {
            if (session != null)
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
    }

    private async Task<ShardState> tryFindExistingState(ShardName shardName, DocumentSessionBase session,
        CancellationToken token)
    {
        var handler = (IQueryHandler<IReadOnlyList<ShardState>>)new ListQueryHandler<ShardState>(
            new ProjectionProgressStatement(_store.Options.EventGraph) { Name = shardName },
            new ShardStateSelector(_store.Options.EventGraph));

        var states = await session.ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
        return states.SingleOrDefault();
    }

    private async Task backfillStreamTypeAliases(long highWaterMark, IAggregateProjection projection,
        CancellationToken token)
    {
        var backfillName = new ShardName(_store.Options.EventGraph.AggregateAliasFor(projection.AggregateType), Backfill);

        var batchSize = 50000L;
        var floor = 0L;
        var ceiling = batchSize;

        // By default, let's do this up to the high water mark
        var backfillThreshold = highWaterMark;

        var options = SessionOptions.ForDatabase(_database);
        options.AllowAnyTenant = true;

        await using var session = (DocumentSessionBase)_store.LightweightSession(options);
        var state = await tryFindExistingState(backfillName, session, token).ConfigureAwait(false);
        if (state != null)
        {
            floor = state.Sequence;
            if (state.Sequence == backfillThreshold) return; // Unnecessary to do anything here
        }
        else
        {
            // Mark this shard as rebuilding
            session.QueueOperation(new MarkShardModeAsRebuilding(backfillName, _store.Events, highWaterMark));
            await session.SaveChangesAsync(token).ConfigureAwait(false);
        }

        _logger.LogInformation("Starting to try to back fill stream aliases for {Aggregate} at Sequence {Sequence}", projection.AggregateType.FullNameInCode(), floor);

        while (!token.IsCancellationRequested && floor < backfillThreshold)
        {
            try
            {
                var op = new BackfillStreamTypeOperation(_logger, floor, ceiling, _store.Options.EventGraph, projection);
                session.QueueOperation(op);
                session.QueueOperation(new UpdateProjectionProgress(_store.Events, new EventRange(backfillName, ceiling)));
                await session.SaveChangesAsync(token).ConfigureAwait(false);

                ceiling += batchSize;
                floor += batchSize;
            }
            catch (TaskCanceledException)
            {
                // Just canceled, get out of here
                return;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error trying to backfill stream type names");
                throw;
            }
        }
    }
}
