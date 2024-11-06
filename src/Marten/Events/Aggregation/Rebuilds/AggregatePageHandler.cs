using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Internals;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Internal.Sessions;
using Marten.Services;
using Marten.Storage;
using Npgsql;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Events.Aggregation.Rebuilds;

internal class AggregatePageHandler<TDoc, TId>
{
    private readonly int _aggregateIndex;
    private readonly NpgsqlCommand _command;
    private readonly IMartenDatabase _database;
    private readonly IReadOnlyList<AggregateIdentity> _ids;
    private readonly IAggregationRuntime<TDoc, TId> _runtime;
    private readonly DocumentSessionBase _session;
    private readonly IEventStorage _storage;
    private readonly DocumentStore _store;

    // If conjoined, the session will hold the tenant id
    public AggregatePageHandler(long ceiling, DocumentStore store, DocumentSessionBase session,
        IAggregationRuntime<TDoc, TId> runtime, IReadOnlyList<AggregateIdentity> ids)
    {
        _store = store;
        _database = session.Database;
        _runtime = runtime;
        _ids = ids;

        _session = session;
        _storage = (IEventStorage)store.Options.Providers.StorageFor<IEvent>().QueryOnly;
        _aggregateIndex = _storage.SelectFields().Length;

        _command = buildFetchCommand(ceiling, store, session, ids);
    }

    private NpgsqlCommand buildFetchCommand(long ceiling, DocumentStore store, DocumentSessionBase session,
        IReadOnlyList<AggregateIdentity> ids)
    {
        var schemaName = store.Options.Events.DatabaseSchemaName;

        var builder = new CommandBuilder();
        builder.Append($"select {_storage.SelectFields().Select(x => "d." + x).Join(", ")}, s.type as stream_type");
        builder.Append(
            $" from {schemaName}.mt_events as d inner join {schemaName}.mt_streams as s on d.stream_id = s.id");

        if (_store.Options.Events.TenancyStyle == TenancyStyle.Conjoined)
        {
            builder.Append(" and d.tenant_id = s.tenant_id");
        }

        builder.Append(" where d.stream_id = Any(");

        if (_store.Options.EventGraph.StreamIdentity == StreamIdentity.AsGuid)
        {
            builder.AppendGuidArrayParameter(ids.Select(x => x.Id).ToArray());
        }
        else
        {
            builder.AppendStringArrayParameter(ids.Select(x => x.Key).ToArray());
        }

        builder.Append($") and d.seq_id <= {ceiling}");

        if (_store.Options.Events.TenancyStyle == TenancyStyle.Conjoined)
        {
            builder.Append(" and s.tenant_id = ");
            builder.AppendParameter(session.TenantId);
            builder.Append(" and d.tenant_id = ");
            builder.AppendParameter(session.TenantId);
        }

        builder.Append(" order by d.stream_id, d.version");

        var command = builder.Compile();
        return command;
    }

    // SAME no matter what
    internal async IAsyncEnumerable<IEvent> ReadEventsAsync(IDaemonRuntime runtime, ShardName shardName,
        ErrorHandlingOptions errorOptions, [EnumeratorCancellation] CancellationToken token)
    {
        var sessionOptions = SessionOptions.ForDatabase(_session.TenantId, _database);

        await using var session = (QuerySession)_store.QuerySession(sessionOptions);
        await using var reader = await session.ExecuteReaderAsync(_command, token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            IEvent @event = null;

            try
            {
                // as a decorator
                @event = await _storage.ResolveAsync(reader, token).ConfigureAwait(false);

                if (!await reader.IsDBNullAsync(_aggregateIndex, token).ConfigureAwait(false))
                {
                    @event.AggregateTypeName =
                        await reader.GetFieldValueAsync<string>(_aggregateIndex, token).ConfigureAwait(false);
                }
            }
            catch (UnknownEventTypeException e)
            {
                if (errorOptions.SkipUnknownEvents)
                {
                    runtime.Logger.EventUnknown(e.EventTypeName);
                }
                else
                {
                    // Let any other exception throw
                    throw;
                }
            }
            catch (EventDeserializationFailureException e)
            {
                if (errorOptions.SkipSerializationErrors)
                {
                    runtime.Logger.EventDeserializationException(e.InnerException!.GetType().Name!, e.Sequence);
                    runtime.Logger.EventDeserializationExceptionDebug(e);
                    await runtime.RecordDeadLetterEventAsync(e.ToDeadLetterEvent(shardName)).ConfigureAwait(false);
                }
                else
                {
                    // Let any other exception throw
                    throw;
                }
            }

            if (@event != null)
            {
                yield return @event;
            }
        }
    }

    public async Task ProcessPageAsync(IDaemonRuntime runtime, ShardName shardName, ErrorHandlingOptions errorOptions,
        CancellationToken token)
    {
        var batch = new ProjectionUpdateBatch(_store.Options.Projections, _session, ShardExecutionMode.Rebuild, token)
        {
            ShouldApplyListeners = false
        };

        // Gotta use the current tenant if using conjoined tenancy
        var sessionOptions = SessionOptions.ForDatabase(_session.TenantId, _session.Database);

        await using var session =
            new ProjectionDocumentSession(_store, batch, sessionOptions, ShardExecutionMode.Rebuild);

        var events = ReadEventsAsync(runtime, shardName, errorOptions, token);

        ITargetBlock<EventSlice<TDoc, TId>> block = new ActionBlock<EventSlice<TDoc, TId>>(async s =>
        {
            // ReSharper disable once AccessToDisposedClosure
            await _runtime.ApplyChangesAsync(session, s, token, ProjectionLifecycle.Async).ConfigureAwait(false);
        });

        await collateAndPostSlices(events, block).ConfigureAwait(false);

        session.QueueOperation(
            new DequeuePendingAggregateRebuilds(_store.Options, _ids.Select(x => x.Number).ToArray()));

        // Wait for all the SQL to be built out to write all operations
        await waitForBatchOperations(block, batch).ConfigureAwait(false);

        // Polly is already around the basic retry here, so anything that gets past this
        // probably deserves a full circuit break
        await session.ExecuteBatchAsync(batch, token).ConfigureAwait(false);
    }

    private static async Task waitForBatchOperations(ITargetBlock<EventSlice<TDoc, TId>> block, ProjectionUpdateBatch batch)
    {
        block.Complete();
        await block.Completion.ConfigureAwait(false);
        await batch.WaitForCompletion().ConfigureAwait(false);
    }

    private async Task collateAndPostSlices(IAsyncEnumerable<IEvent> events, ITargetBlock<EventSlice<TDoc, TId>> block)
    {
        EventSlice<TDoc, TId> slice = null;
        await foreach (var e in events)
        {
            var aggregateId = _runtime.IdentityFromEvent(e);
            slice ??= new EventSlice<TDoc, TId>(aggregateId, _session.TenantId);

            if (!slice.Id.Equals(aggregateId))
            {
                block.Post(slice);
                slice = new EventSlice<TDoc, TId>(aggregateId, _session.TenantId);
            }

            slice.AddEvent(e);
        }

        // Get the last one
        block.Post(slice);
    }
}
