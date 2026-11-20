#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using JasperFx.Events.Tags;
using Marten.Events.Archiving;
using Marten.Events.Schema;
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

    public Task<IReadOnlyList<IEvent>> LoadAsync(IdentityRebuildEventRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);

        var streamId = StreamIdFor(request.StreamIdentity, _store.Options.EventGraph.StreamIdentity);
        return loadAsync(new Spec(IdentityEventSource.Stream, request.TenantId, request.AfterSequence,
            request.Ceiling, request.Limit, request.SkipSerializationErrors, false)
        {
            StreamId = streamId,
            Filters = filtersFor(request).ToArray(),
            IncludeArchivedEvents = request.IncludeArchivedEvents
        }, token);
    }

    /// <summary>
    ///     marten#6003: one identity's events from <see cref="IdentityEventLoadRequest.Source" />, the store side of
    ///     <c>IEventStore.LoadEventsForIdentityAsync</c>.
    /// </summary>
    public Task<IReadOnlyList<IEvent>> LoadAsync(IdentityEventLoadRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);

        var spec = new Spec(request.Source, request.TenantId, request.AfterSequence, request.Ceiling, request.Limit,
            request.SkipSerializationErrors, request.IncludeTags || request.Source == IdentityEventSource.Tag)
        {
            Filters = _store.buildEventLoaderFilters(request.EventTypes, null, request.IncludeArchivedEvents, [])
                .ToArray(),
            IncludeArchivedEvents = request.IncludeArchivedEvents
        };

        switch (request.Source)
        {
            case IdentityEventSource.Stream:
                spec = spec with
                {
                    StreamId = StreamIdFor(request.Identity ?? throw identityRequired(request),
                        _store.Options.EventGraph.StreamIdentity)
                };
                break;

            case IdentityEventSource.Tag:
                var registration = _store.Options.EventGraph.FindTagType(request.TagType ??
                                                                         throw new ArgumentException(
                                                                             "A tag source needs the tag type",
                                                                             nameof(request)))
                                   ?? throw new InvalidOperationException(
                                       $"Tag type '{request.TagType.FullNameInCode()}' is not registered. Call RegisterTagType<{request.TagType.Name}>() first.");

                spec = spec with
                {
                    Tag = registration,
                    IdentityValue = request.IdentityValue ?? throw identityRequired(request)
                };
                break;

            case IdentityEventSource.SliceIndex:
                spec = spec with
                {
                    SliceIndexTable = SliceIndexTable.NameFor(
                        request.ProjectionName ?? throw new ArgumentException(
                            "A slice index source needs the projection name", nameof(request)),
                        request.ProjectionVersion),
                    IdentityValue = request.IdentityValue ?? throw identityRequired(request)
                };
                break;
        }

        return loadAsync(spec, token);
    }

    private static ArgumentException identityRequired(IdentityEventLoadRequest request)
        => new($"Reading one identity's events from the {request.Source} source needs the identity", nameof(request));

    // What to read, whatever asked for it
    private sealed record Spec(
        IdentityEventSource Source,
        string? TenantId,
        long AfterSequence,
        long Ceiling,
        int Limit,
        bool SkipSerializationErrors,
        bool IncludeTags)
    {
        public ISqlFragment[] Filters { get; init; } = [];
        public bool IncludeArchivedEvents { get; init; }
        public object? StreamId { get; init; }
        public ITagTypeRegistration? Tag { get; init; }
        public string? SliceIndexTable { get; init; }
        public object? IdentityValue { get; init; }
    }

    private async Task<IReadOnlyList<IEvent>> loadAsync(Spec spec, CancellationToken token)
    {
        var limit = Math.Max(spec.Limit, 1);
        var errors = _store.Options.Projections.Errors;

        var storage = (IEventStorage)_store.Options.Providers.StorageFor<IEvent>().QueryOnly;
        var aggregateIndex = storage.SelectFields().Length;
        var sequenceIndex = Array.IndexOf(storage.SelectFields(), "seq_id");

        var options = SessionOptions.ForDatabase(_database);
        options.ReadFromPrimary = true;
        await using var session = (QuerySession)_store.QuerySession(options);

        var loaded = new List<IEvent>();
        var floor = spec.AfterSequence;

        while (loaded.Count < limit)
        {
            var wanted = limit - loaded.Count;
            var command = buildCommand(storage, spec, floor, wanted);

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
                    // marten#6003 (decision 12): the request's own choice, never the store's continuous options, so a
                    // replay can't silently skip the unreadable event it was asked to repair
                    catch (EventDeserializationFailureException e) when (spec.SkipSerializationErrors)
                    {
                        floor = e.Sequence;
                    }
                }
            }

            // Fewer rows than asked for means the source has nothing more in the range
            if (rows < wanted) break;
        }

        if (spec.IncludeTags)
        {
            await EventTagHydrator.HydrateAsync(_store.Options.EventGraph, session, loaded, token)
                .ConfigureAwait(false);
        }

        return loaded;
    }

    private Npgsql.NpgsqlCommand buildCommand(IEventStorage storage, Spec spec, long floor, int limit)
    {
        var events = _store.Options.Events;
        var schemaName = events.DatabaseSchemaName;
        var conjoined = events.TenancyStyle == TenancyStyle.Conjoined;

        var builder = new CommandBuilder();
        builder.Append($"select {storage.SelectFields().Select(x => "d." + x).Join(", ")}, s.type as stream_type");
        builder.Append($" from {schemaName}.mt_events as d inner join {schemaName}.mt_streams as s on d.stream_id = s.id");

        if (conjoined)
        {
            builder.Append(" and d.tenant_id = s.tenant_id");
        }

        if (events.UseArchivedStreamPartitioning && !spec.IncludeArchivedEvents)
        {
            // #4745: prune the archived mt_streams partition, as the daemon loader does
            builder.Append($" and s.{IsArchivedColumn.ColumnName} = FALSE");
        }

        builder.Append(" where d.seq_id > ");
        builder.AppendParameter(floor, NpgsqlDbType.Bigint);
        builder.Append(" and d.seq_id <= ");
        builder.AppendParameter(spec.Ceiling, NpgsqlDbType.Bigint);

        switch (spec.Source)
        {
            case IdentityEventSource.Stream:
                builder.Append(" and d.stream_id = ");
                if (spec.StreamId is Guid guid)
                {
                    builder.AppendParameter(guid, NpgsqlDbType.Uuid);
                }
                else
                {
                    builder.AppendParameter((string)spec.StreamId!, NpgsqlDbType.Varchar);
                }

                appendTenant(builder, spec, conjoined);
                break;

            case IdentityEventSource.Tag:
                appendTag(builder, spec, conjoined);
                appendTenant(builder, spec, conjoined);
                break;

            case IdentityEventSource.SliceIndex:
                // The events the index lists for the identity, whatever tenant the event itself is in. Under per-tenant
                // event partitioning a sequence is only unique within its tenant, so the slice's tenant it is.
                builder.Append($" and d.seq_id in (select i.seq_id from {schemaName}.{spec.SliceIndexTable} as i where i.tenant_id = ");
                builder.AppendParameter(spec.TenantId!, NpgsqlDbType.Varchar);
                builder.Append(" and i.identity = ");
                appendIdentityValue(builder, spec.IdentityValue!);
                builder.Append(')');

                if (events.UseTenantPartitionedEvents)
                {
                    appendTenant(builder, spec, conjoined);
                }

                break;

            case IdentityEventSource.Scan:
                appendTenant(builder, spec, conjoined);
                break;
        }

        foreach (var filter in spec.Filters)
        {
            builder.Append(" and ");
            filter.Apply(builder);
        }

        builder.Append(" order by d.seq_id limit ");
        builder.Append(limit);

        return builder.Compile();
    }

    private static void appendTenant(CommandBuilder builder, Spec spec, bool conjoined)
    {
        if (!conjoined || spec.TenantId == null) return;

        builder.Append(" and d.tenant_id = ");
        builder.AppendParameter(spec.TenantId, NpgsqlDbType.Varchar);
    }

    // The events carrying a tag of the type with the identity's value, through the tag table or the tag column
    private void appendTag(CommandBuilder builder, Spec spec, bool conjoined)
    {
        var events = _store.Options.EventGraph;
        var registration = spec.Tag!;

        if (events.DcbStorageMode == DcbStorageMode.HStore)
        {
            builder.Append(" and d.tags @> hstore(");
            builder.AppendParameter(registration.TableSuffix, NpgsqlDbType.Text);
            builder.Append(", ");
            builder.AppendParameter(Convert.ToString(spec.IdentityValue, CultureInfo.InvariantCulture)!,
                NpgsqlDbType.Text);
            builder.Append(')');
            return;
        }

        builder.Append(
            $" and d.seq_id in (select t.seq_id from {events.DatabaseSchemaName}.mt_event_tag_{registration.TableSuffix} as t where t.value = ");
        appendIdentityValue(builder, spec.IdentityValue!);
        if (conjoined && spec.TenantId != null)
        {
            builder.Append(" and t.tenant_id = ");
            builder.AppendParameter(spec.TenantId, NpgsqlDbType.Varchar);
        }

        builder.Append(')');
    }

    private static void appendIdentityValue(CommandBuilder builder, object value)
    {
        switch (value)
        {
            case Guid guid:
                builder.AppendParameter(guid, NpgsqlDbType.Uuid);
                break;
            case int number:
                builder.AppendParameter(number, NpgsqlDbType.Integer);
                break;
            case long number:
                builder.AppendParameter(number, NpgsqlDbType.Bigint);
                break;
            case short number:
                builder.AppendParameter(number, NpgsqlDbType.Smallint);
                break;
            default:
                builder.AppendParameter(Convert.ToString(value, CultureInfo.InvariantCulture)!, NpgsqlDbType.Varchar);
                break;
        }
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
