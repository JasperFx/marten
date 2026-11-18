#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Archiving;
using Marten.Exceptions;
using Marten.Internal.Sessions;
using Marten.Linq.Selectors;
using Marten.Services;
using Marten.Storage;
using NpgsqlTypes;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Events.Daemon.Internals;

/// <summary>
///     Reads one stream's events for a per-identity rebuild's re-fold (marten#6071, jasperfx#1201): the store side of
///     <c>IEventStore.LoadEventsForIdentityRebuildAsync</c>.
/// </summary>
/// <remarks>
///     <para>
///         The same rules as <see cref="EventLoader" />, so the re-fold writes what the shard would have: the same
///         filters (the projection's event types with <see cref="Archived" /> and <c>Compacted&lt;T&gt;</c>, an
///         interface or abstract type reading every event, archived events only when the projection reads them,
///         skipped events left out), the same join to <c>mt_streams</c> with the archived-partition predicate, the
///         tenant under conjoined tenancy, and the same event storage reading the rows, so deserialization and
///         upcasting are the daemon's. Read from the primary, as the daemon reads.
///     </para>
///     <para>
///         A short answer means "no more". JasperFx stops paging when it gets fewer events than it asked for, so a
///         row skipped under <c>SkipUnknownEvents</c> or <c>SkipSerializationErrors</c> does not shorten the
///         answer: the read goes on until it has the limit or runs out of rows. A row the shard would not skip
///         throws, which faults the rebuild request and leaves the shard running. Nothing is dead-lettered here,
///         because the shard already did that when it met the row.
///     </para>
/// </remarks>
[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: hydrates IEvent rows through the event storage, as EventLoader does. Event types are preserved by EventGraph registration per the AOT publishing guide.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: event deserialization, as in EventLoader. AOT consumers register concrete event types ahead of time.")]
internal sealed class IdentityRebuildEventLoader
{
    private readonly DocumentStore _store;
    private readonly MartenDatabase _database;

    public IdentityRebuildEventLoader(DocumentStore store, MartenDatabase database)
    {
        _store = store;
        _database = database;
    }

    public async Task<IReadOnlyList<IEvent>> LoadAsync(IdentityRebuildEventRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);

        var events = _store.Options.EventGraph;
        var streamId = StreamIdFor(request.StreamIdentity, events.StreamIdentity);
        var limit = Math.Max(request.Limit, 1);
        var errors = _store.Options.Projections.Errors;

        var storage = (IEventStorage)_store.Options.Providers.StorageFor<IEvent>().QueryOnly;
        var aggregateIndex = storage.SelectFields().Length;
        var sequenceIndex = Array.IndexOf(storage.SelectFields(), "seq_id");
        var filters = filtersFor(request).ToArray();

        var options = SessionOptions.ForDatabase(_database);
        options.ReadFromPrimary = true;
        await using var session = (QuerySession)_store.QuerySession(options);

        var loaded = new List<IEvent>();
        var floor = request.AfterSequence;

        while (loaded.Count < limit)
        {
            var wanted = limit - loaded.Count;
            var command = buildCommand(storage, filters, request, streamId, floor, wanted);

            var rows = 0;
            await using (var reader = await session.ExecuteReaderAsync(command, token).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    rows++;

                    try
                    {
                        var @event = await ((ISelector<IEvent>)storage).ResolveAsync(reader, token)
                            .ConfigureAwait(false);
                        floor = @event.Sequence;

                        if (!await reader.IsDBNullAsync(aggregateIndex, token).ConfigureAwait(false))
                        {
                            @event.AggregateTypeName =
                                await reader.GetFieldValueAsync<string>(aggregateIndex, token).ConfigureAwait(false);
                        }

                        loaded.Add(@event);
                    }
                    catch (UnknownEventTypeException) when (errors.SkipUnknownEvents)
                    {
                        floor = await reader.GetFieldValueAsync<long>(sequenceIndex, token).ConfigureAwait(false);
                    }
                    catch (EventDeserializationFailureException e) when (errors.SkipSerializationErrors)
                    {
                        floor = e.Sequence;
                    }
                }
            }

            // Fewer rows than asked for means the stream has nothing more in the range
            if (rows < wanted) break;
        }

        return loaded;
    }

    private Npgsql.NpgsqlCommand buildCommand(IEventStorage storage, ISqlFragment[] filters,
        IdentityRebuildEventRequest request, object streamId, long floor, int limit)
    {
        var events = _store.Options.Events;
        var schemaName = events.DatabaseSchemaName;

        var builder = new CommandBuilder();
        builder.Append($"select {storage.SelectFields().Select(x => "d." + x).Join(", ")}, s.type as stream_type");
        builder.Append($" from {schemaName}.mt_events as d inner join {schemaName}.mt_streams as s on d.stream_id = s.id");

        if (events.TenancyStyle == TenancyStyle.Conjoined)
        {
            builder.Append(" and d.tenant_id = s.tenant_id");
        }

        if (events.UseArchivedStreamPartitioning && !request.IncludeArchivedEvents)
        {
            // #4745: prune the archived mt_streams partition, as the daemon loader does
            builder.Append($" and s.{IsArchivedColumn.ColumnName} = FALSE");
        }

        builder.Append(" where d.seq_id > ");
        builder.AppendParameter(floor, NpgsqlDbType.Bigint);
        builder.Append(" and d.seq_id <= ");
        builder.AppendParameter(request.Ceiling, NpgsqlDbType.Bigint);

        builder.Append(" and d.stream_id = ");
        if (streamId is Guid guid)
        {
            builder.AppendParameter(guid, NpgsqlDbType.Uuid);
        }
        else
        {
            builder.AppendParameter((string)streamId, NpgsqlDbType.Varchar);
        }

        if (events.TenancyStyle == TenancyStyle.Conjoined)
        {
            builder.Append(" and d.tenant_id = ");
            builder.AppendParameter(request.TenantId, NpgsqlDbType.Varchar);
        }

        foreach (var filter in filters)
        {
            builder.Append(" and ");
            filter.Apply(builder);
        }

        builder.Append(" order by d.seq_id limit ");
        builder.Append(limit);

        return builder.Compile();
    }

    private IEnumerable<ISqlFragment> filtersFor(IdentityRebuildEventRequest request)
    {
        // The request carries the projection's event types but not the projection, so find the single-stream
        // aggregations it can be: that is where the daemon's Compacted<T> and stream type filters come from
        var candidates = _store.Options.Projections.All
            .Where(x => x is IAggregateProjection { Scope: AggregationScope.SingleStream })
            .OfType<EventFilterable>()
            .Where(x => x.IncludeArchivedEvents == request.IncludeArchivedEvents
                        && x.IncludedEventTypes.ToHashSet().SetEquals(request.EventTypes))
            .ToArray();

        var aggregateTypes = candidates.OfType<IAggregateProjection>().Select(x => x.AggregateType).Distinct()
            .ToArray();

        var streamTypes = candidates.Select(x => x.StreamType).Distinct().ToArray();
        var streamType = streamTypes.Length == 1 ? streamTypes[0] : null;

        return _store.buildEventLoaderFilters(request.EventTypes, streamType, request.IncludeArchivedEvents,
            aggregateTypes);
    }

    // The projected identity as the projection knows it: the stream's Guid or string, or a strong-typed id
    // wrapping one
    internal static object StreamIdFor(object identity, StreamIdentity streamIdentity)
    {
        var streamIdType = streamIdentity == StreamIdentity.AsGuid ? typeof(Guid) : typeof(string);

        if (identity.GetType() == streamIdType) return identity;

        if (StrongTypedIdResolver.TryResolve(identity.GetType(), out var info) && info.SimpleType == streamIdType)
        {
            return StrongTypedIdResolver.Unwrap(info, identity);
        }

        throw new ArgumentOutOfRangeException(nameof(identity),
            $"A per-identity rebuild was asked for '{identity}' ({identity.GetType().FullNameInCode()}), but this store's "
            + $"stream identity is {streamIdType.Name}. Pass a {streamIdType.Name} or a strong-typed id wrapping one.");
    }
}
