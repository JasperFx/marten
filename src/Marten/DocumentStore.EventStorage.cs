using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Archiving;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Internals;
using Marten.Events.Daemon.Progress;
using Marten.Internal.OpenTelemetry;
using Marten.Internal.Sessions;
using Marten.Schema;
using Marten.Services;
using Marten.Storage;
using Marten.Subscriptions;
using Microsoft.Extensions.Logging;
using Weasel.Postgresql.SqlGeneration;
using EventTypeFilter = Marten.Events.Daemon.Internals.EventTypeFilter;

namespace Marten;

public partial class DocumentStore: IEventStorage<IDocumentOperations, IQuerySession>, ISubscriptionRunner<ISubscription>
{
    IEventRegistry IEventStorage<IDocumentOperations, IQuerySession>.Registry => Options.EventGraph;

    public Type IdentityTypeForProjectedType(Type aggregateType)
    {
        return new DocumentMapping(aggregateType, Options).DocumentType;
    }

    string IEventStorage<IDocumentOperations, IQuerySession>.DefaultDatabaseName =>
        Options.Tenancy.Default.Database.Identifier;

    ErrorHandlingOptions IEventStorage<IDocumentOperations, IQuerySession>.ContinuousErrors =>
        Options.Projections.Errors;

    ErrorHandlingOptions IEventStorage<IDocumentOperations, IQuerySession>.RebuildErrors =>
        Options.Projections.RebuildErrors;

    IReadOnlyList<AsyncShard<IDocumentOperations, IQuerySession>> IEventStorage<IDocumentOperations, IQuerySession>.
        AllShards()
    {
        return Options.Projections.AllShards();
    }

    Meter IEventStorage<IDocumentOperations, IQuerySession>.Meter => Options.OpenTelemetry.Meter;

    ActivitySource IEventStorage<IDocumentOperations, IQuerySession>.ActivitySource => MartenTracing.ActivitySource;

    TimeProvider IEventStorage<IDocumentOperations, IQuerySession>.TimeProvider => Options.Events.TimeProvider;

    string IEventStorage<IDocumentOperations, IQuerySession>.MetricsPrefix => "marten";

    AutoCreate IEventStorage<IDocumentOperations, IQuerySession>.AutoCreateSchemaObjects =>
        Options.AutoCreateSchemaObjects;

    async Task IEventStorage<IDocumentOperations, IQuerySession>.RewindSubscriptionProgressAsync(
        IEventDatabase database, string subscriptionName, CancellationToken token,
        long? sequenceFloor)
    {
        var sessionOptions = SessionOptions.ForDatabase((IMartenDatabase)database);
        sessionOptions.AllowAnyTenant = true;
        await using var session = LightweightSession(sessionOptions);

        var names = Options
            .Projections
            .AllShards()
            .Where(x => x.Name.Identity.EqualsIgnoreCase(subscriptionName))
            .Select(x => x.Name)
            .ToArray();

        foreach (var name in names)
        {
            if (sequenceFloor.Value == 0)
            {
                session.QueueSqlCommand($"delete from {Options.EventGraph.ProgressionTable} where name = ?",
                    name.Identity);
            }
            else
            {
                session.QueueSqlCommand(
                    $"insert into {Options.EventGraph.ProgressionTable} (name, last_seq_id) values (?, ?) on conflict (name) do update set last_seq_id = ?",
                    name.Identity, sequenceFloor, sequenceFloor);
            }
        }

        // Rewind previous DeadLetterEvents because you're going to replay them all anyway
        session.DeleteWhere<DeadLetterEvent>(x =>
            x.ProjectionName == subscriptionName && x.EventSequence >= sequenceFloor);

        await session.SaveChangesAsync(token).ConfigureAwait(false);
    }

    async Task IEventStorage<IDocumentOperations, IQuerySession>.RewindAgentProgressAsync(IEventDatabase database,
        string shardName, CancellationToken token, long sequenceFloor)
    {
        var sessionOptions = SessionOptions.ForDatabase((IMartenDatabase)database);
        sessionOptions.AllowAnyTenant = true;
        await using var session = LightweightSession(sessionOptions);

        if (sequenceFloor > 0)
        {
            session.QueueSqlCommand(
                $"insert into {Options.EventGraph.ProgressionTable} (name, last_seq_id) values (?, ?) on conflict (name) do update set last_seq_id = ?",
                shardName, sequenceFloor, sequenceFloor);
        }

        await session.SaveChangesAsync(token).ConfigureAwait(false);
    }

    async Task IEventStorage<IDocumentOperations, IQuerySession>.TeardownExistingProjectionProgressAsync(
        IEventDatabase database, string subscriptionName, CancellationToken token)
    {
        var sessionOptions = SessionOptions.ForDatabase((IMartenDatabase)database);
        sessionOptions.AllowAnyTenant = true;
        await using var session = LightweightSession(sessionOptions);

        var source = Options.Projections.All.FirstOrDefault(x => x.ProjectionName.EqualsIgnoreCase(subscriptionName));

        if (source == null)
        {
            throw new ArgumentOutOfRangeException(nameof(subscriptionName));
        }

        if (source.Options.TeardownDataOnRebuild)
        {
            source.Options.Teardown(session);
        }

        foreach (var agent in source.Shards())
            session.QueueOperation(new DeleteProjectionProgress(Events, agent.Name.Identity));

        // Rewind previous DeadLetterEvents because you're going to replay them all anyway
        session.DeleteWhere<DeadLetterEvent>(x => x.ProjectionName == source.ProjectionName);

        await session.SaveChangesAsync(token).ConfigureAwait(false);
    }

    public async ValueTask<IProjectionBatch<IDocumentOperations, IQuerySession>> StartProjectionBatchAsync(EventRange range, IEventDatabase database, ShardExecutionMode mode,
        CancellationToken token)
    {
        await database.EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);
        var session = (DocumentSessionBase)IdentitySession(SessionOptions.ForDatabase((IMartenDatabase)database));
        var batch = new ProjectionUpdateBatch(Options.Projections, session, ShardExecutionMode.Rebuild, token)
        {
            ShouldApplyListeners = false
        };

        var projectionBatch = new ProjectionBatch(session, batch, mode);
        if (range.SequenceFloor == 0)
        {
            batch.Queue.Post(new InsertProjectionProgress(session.Options.EventGraph, range));
        }
        else
        {
            batch.Queue.Post(new UpdateProjectionProgress(session.Options.EventGraph, range));
        }

        return projectionBatch;
    }

    IEventLoader IEventStorage<IDocumentOperations, IQuerySession>.BuildEventLoader(IEventDatabase database,
        ILogger loggerFactory, EventFilterable filtering, AsyncOptions shardOptions)
    {
        var filters = buildEventLoaderFilters(filtering).ToArray();
        var inner = new EventLoader(this, (MartenDatabase)database, shardOptions, filters);
        return new ResilientEventLoader(Options.ResiliencePipeline, inner);
    }

    private IEnumerable<ISqlFragment> buildEventLoaderFilters(EventFilterable filterable)
    {
        if (filterable.IncludedEventTypes.Any() && !filterable.IncludedEventTypes.Any(x => x.IsAbstract || x.IsInterface))
        {
            // We want to explicitly add in the archived event
            var allTypes = filterable.IncludedEventTypes.Concat([typeof(Archived)]).ToArray();
            yield return new EventTypeFilter(Options.EventGraph, allTypes);
        }

        if (filterable.StreamType != null)
        {
            yield return new AggregateTypeFilter(filterable.StreamType, Options.EventGraph);
        }

        if (!filterable.IncludeArchivedEvents)
        {
            yield return IsNotArchivedFilter.Instance;
        }
    }

    IDocumentOperations IEventStorage<IDocumentOperations, IQuerySession>.OpenSession(IEventDatabase database)
    {
        return LightweightSession(SessionOptions.ForDatabase((IMartenDatabase)database));
    }

    IDocumentOperations IEventStorage<IDocumentOperations, IQuerySession>.OpenSession(IEventDatabase database,
        string tenantId)
    {
        return LightweightSession(SessionOptions.ForDatabase(tenantId, (IMartenDatabase)database));
    }

    ErrorHandlingOptions IEventStorage<IDocumentOperations, IQuerySession>.ErrorHandlingOptions(ShardExecutionMode mode)
    {
        return mode == ShardExecutionMode.Rebuild ? Options.Projections.RebuildErrors : Options.Projections.Errors;
    }

    async Task ISubscriptionRunner<ISubscription>.ExecuteAsync(ISubscription subscription, IEventDatabase database, EventRange range, ShardExecutionMode mode,
        CancellationToken token)
    {
        var db = (IMartenDatabase)database;
        await using var parent = (DocumentSessionBase)OpenSession(SessionOptions.ForDatabase(db));

        var batch = new ProjectionUpdateBatch(Options.Projections, parent, mode, token)            {
            ShouldApplyListeners = mode == ShardExecutionMode.Continuous && range.Events.Any()
        };;

        // Mark the progression
        batch.Queue.Post(range.BuildProgressionOperation(Events));

        await using var session = new ProjectionDocumentSession(this, batch,
            new SessionOptions
            {
                Tracking = DocumentTracking.IdentityOnly,
                Tenant = new Tenant(StorageConstants.DefaultTenantId, db)
            }, mode);


        var listener = await subscription.ProcessEventsAsync(range, range.Agent, session, token)
            .ConfigureAwait(false);

        batch.Listeners.Add(listener);
        await batch.WaitForCompletion().ConfigureAwait(false);

        // Polly is already around the basic retry here, so anything that gets past this
        // probably deserves a full circuit break
        await session.ExecuteBatchAsync(batch, token).ConfigureAwait(false);
    }
}
