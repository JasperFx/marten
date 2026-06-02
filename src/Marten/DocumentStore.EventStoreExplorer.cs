#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Descriptors;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Internal.Sessions;
using Marten.Services;
using Marten.Storage;
using Npgsql;
using System.Diagnostics.CodeAnalysis;

namespace Marten;

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
[UnconditionalSuppressMessage("Trimming", "IL2060",
    Justification = "Class-level: Expression.Call(Type, string, ...) on framework Queryable / Enumerable intrinsics that the trimmer preserves.")]
[UnconditionalSuppressMessage("Trimming", "IL2067",
    Justification = "Class-level: parameter receives a DAM-annotated Type from a reflective lookup whose source type is preserved at the StoreOptions / projection-registration boundary.")]
[UnconditionalSuppressMessage("Trimming", "IL2070",
    Justification = "Class-level: reflects PublicMethods/PublicProperties on a Type whose runtime instance is preserved at the StoreOptions / projection-registration boundary.")]
[UnconditionalSuppressMessage("Trimming", "IL2075",
    Justification = "Class-level: PublicMethods/PublicProperties access via a Type obtained from object.GetType() / GetGenericArguments. Source instance is preserved at the StoreOptions / projection-registration boundary.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
public partial class DocumentStore
{
    /// <summary>
    /// Hard cap on the count <see cref="GetRecentStreamsAsync"/> will honour, regardless
    /// of the value the caller passes in. Bounds the explorer's stream-list view at a
    /// page size that any single Postgres index scan can satisfy without surprise cost.
    /// </summary>
    private const int RecentStreamsCap = 1000;

    private static readonly JsonSerializerOptions ExplorerJson = new()
    {
        PropertyNamingPolicy = null
    };

    /// <inheritdoc />
    /// <remarks>
    /// Returns the most recently updated streams across every tenant, ordered by the
    /// stream's <c>timestamp</c> column (last append) descending. Hard-capped at
    /// <see cref="RecentStreamsCap"/> regardless of the requested count to keep the
    /// explorer's stream-list view bounded. Archived streams are included.
    /// </remarks>
    async Task<IReadOnlyList<StreamSummary>> IEventStore.GetRecentStreamsAsync(int count, CancellationToken ct)
    {
        var limit = Math.Clamp(count, 0, RecentStreamsCap);
        if (limit == 0) return Array.Empty<StreamSummary>();

        await using var session = openExplorerSession();
        await session.Database.EnsureStorageExistsAsync(typeof(IEvent), ct).ConfigureAwait(false);

        // The leading six columns deliberately mirror IEventStorage.StreamStateSelectSql
        // (#4359) so readStreamRowAsync below can read them with the same column
        // ordering the codegen path uses for FetchStreamStateAsync. The
        // event_storage_stream_state_selector test (added in #4359) enforces the column
        // list against the selector's Resolve method; this site's matching SELECT is
        // verified by the explorer tests in EventSourcingTests/Explorer.
        var schema = Options.EventGraph.DatabaseSchemaName;
        var cmd = new NpgsqlCommand(
            $"select id, version, type, timestamp, created, is_archived, tenant_id from {schema}.mt_streams " +
            "order by timestamp desc limit @limit");
        cmd.Parameters.AddWithValue("limit", limit);

        var summaries = new List<StreamSummary>(limit);
        await using var reader = await session.ExecuteReaderAsync(cmd, ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var row = await readStreamRowAsync(reader, ct).ConfigureAwait(false);
            var tenantId = await reader.IsDBNullAsync(StreamStateColumnCount, ct).ConfigureAwait(false)
                ? null
                : reader.GetString(StreamStateColumnCount);

            summaries.Add(new StreamSummary(row.StreamId, row.StreamType, row.Version, row.Created, row.LastUpdated, tenantId));
        }

        return summaries;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Streams the events of a single stream from oldest to newest as raw JSON, suitable
    /// for the explorer's stream-detail timeline view. The body and metadata are returned
    /// as untyped <see cref="JsonElement"/> instances so the descriptor stays untyped at
    /// the JasperFx layer; tag data is intentionally <see langword="null"/> here because
    /// the per-event tag join would multiply the row cost — explorers that need tags
    /// should use <c>QueryByTagsAsync</c>.
    /// </remarks>
    async IAsyncEnumerable<EventRecord> IEventStore.ReadStreamAsync(string streamId, [EnumeratorCancellation] CancellationToken ct)
    {
        var schema = Options.EventGraph.DatabaseSchemaName;
        var sql =
            $"select id, seq_id, version, stream_id, type, data, timestamp, tenant_id " +
            $"from {schema}.mt_events " +
            $"where stream_id = @stream_id " +
            $"order by version asc";

        await using var session = openExplorerSession();
        await session.Database.EnsureStorageExistsAsync(typeof(IEvent), ct).ConfigureAwait(false);

        var cmd = new NpgsqlCommand(sql);
        cmd.Parameters.AddWithValue("stream_id", parseStreamId(streamId));

        await using var reader = await session.ExecuteReaderAsync(cmd, ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            yield return await readEventRecordAsync(reader, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns full diagnostic metadata for a single stream — version, timestamps,
    /// archive flag, tenant. <see cref="StreamMetadata.SnapshotVersion"/> and
    /// <see cref="StreamMetadata.LastSnapshotAt"/> are always <see langword="null"/> on
    /// Marten 9.0+ because <c>mt_streams</c> no longer carries snapshot-version columns
    /// (#4316). The Tags map is empty because Marten stores tags per-event rather than
    /// per-stream; future work could surface the union of tags applied to the stream's
    /// events. Returns <see langword="null"/> when no row exists for the requested id.
    /// </remarks>
    async Task<StreamMetadata?> IEventStore.GetStreamMetadataAsync(string streamId, CancellationToken ct)
    {
        await using var session = openExplorerSession();
        await session.Database.EnsureStorageExistsAsync(typeof(IEvent), ct).ConfigureAwait(false);

        // Same shared-column-ordering story as GetRecentStreamsAsync — the leading
        // six columns match IEventStorage.StreamStateSelectSql (#4359). The trailing
        // ordinals are addressed off StreamStateColumnCount so any future change to
        // the shared selector's column count surfaces here as a single update site.
        var schema = Options.EventGraph.DatabaseSchemaName;
        var cmd = new NpgsqlCommand(
            $"select id, version, type, timestamp, created, is_archived, tenant_id from {schema}.mt_streams " +
            "where id = @stream_id limit 1");
        cmd.Parameters.AddWithValue("stream_id", parseStreamId(streamId));

        await using var reader = await session.ExecuteReaderAsync(cmd, ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;

        var row = await readStreamRowAsync(reader, ct).ConfigureAwait(false);
        var tenantId = await reader.IsDBNullAsync(StreamStateColumnCount, ct).ConfigureAwait(false)
            ? null
            : reader.GetString(StreamStateColumnCount);

        return new StreamMetadata(
            row.StreamId,
            row.StreamType,
            row.Version,
            row.Created,
            row.LastUpdated,
            // LastSnapshotAt / LastSnapshotVersion are null under Marten 9.0+ —
            // the mt_streams snapshot columns were dropped in #4316 as vestigial.
            LastSnapshotAt: null,
            LastSnapshotVersion: null,
            row.IsArchived,
            tenantId,
            new Dictionary<string, string>(0));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Streams events whose tag set matches every entry in <paramref name="tags"/> (AND
    /// semantics across keys). Each key is the tag-type's CLR short name (e.g.
    /// <c>"StudentId"</c>) — looked up against <see cref="EventGraph.TagTypes"/>. Values
    /// are compared as text against the <c>value</c> column of each tag table; this is
    /// permissive (works for string, Guid, integer tag inner types) at the cost of always
    /// going through Postgres' text cast. Throws <see cref="ArgumentException"/> when a
    /// tag name is not registered.
    /// </remarks>
    async IAsyncEnumerable<EventRecord> IEventStore.QueryByTagsAsync(
        IReadOnlyDictionary<string, string> tags,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (tags == null) throw new ArgumentNullException(nameof(tags));

        await using var session = openExplorerSession();
        await session.Database.EnsureStorageExistsAsync(typeof(IEvent), ct).ConfigureAwait(false);

        if (tags.Count == 0) yield break;

        var schema = Options.EventGraph.DatabaseSchemaName;
        var registered = Options.EventGraph.TagTypes;

        var subqueries = new List<string>(tags.Count);
        var parameters = new List<NpgsqlParameter>(tags.Count);
        var idx = 0;
        foreach (var (tagName, tagValue) in tags)
        {
            var registration = registered.FirstOrDefault(r => string.Equals(r.TagType.Name, tagName, StringComparison.OrdinalIgnoreCase));
            if (registration == null)
            {
                throw new ArgumentException(
                    $"Tag type '{tagName}' is not registered on this event store. Registered tag types: {registered.Select(t => t.TagType.Name).Join(", ")}",
                    nameof(tags));
            }

            var tagTable = $"{schema}.mt_event_tag_{registration.TableSuffix}";
            var paramName = $"@tag_value_{idx}";
            subqueries.Add($"e.seq_id in (select seq_id from {tagTable} where value::text = {paramName})");
            parameters.Add(new NpgsqlParameter($"tag_value_{idx}", tagValue));
            idx++;
        }

        var sql =
            $"select e.id, e.seq_id, e.version, e.stream_id, e.type, e.data, e.timestamp, e.tenant_id " +
            $"from {schema}.mt_events e " +
            $"where {subqueries.Join(" and ")} " +
            $"order by e.seq_id asc";

        var cmd = new NpgsqlCommand(sql);
        foreach (var p in parameters) cmd.Parameters.Add(p);

        await using var reader = await session.ExecuteReaderAsync(cmd, ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            yield return await readEventRecordAsync(reader, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Looks up the named projection on <see cref="StoreOptions.Projections"/>, runs the
    /// tag-matched events through it via Marten's aggregator infrastructure, and returns
    /// the projected state serialized as JSON. Returns <see langword="null"/> when the
    /// projection is not registered or no events match the tag set.
    /// </remarks>
    async Task<DcbProjectedState?> IEventStore.GetProjectedStateForTagsAsync(
        string projectionName,
        IReadOnlyDictionary<string, string> tags,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(projectionName)) throw new ArgumentNullException(nameof(projectionName));

        var source = Options.Projections.All.FirstOrDefault(x => x.Name.EqualsIgnoreCase(projectionName));
        if (source is not IAggregateProjection aggregate) return null;

        var events = new List<EventRecord>();
        await foreach (var ev in ((IEventStore)this).QueryByTagsAsync(tags, ct).ConfigureAwait(false))
        {
            events.Add(ev);
        }
        if (events.Count == 0) return null;

        var aggregateType = aggregate.AggregateType;
        var (state, _) = await replayAggregatorAsync(aggregateType, startingState: null, events, ct).ConfigureAwait(false);
        if (state == null) return null;

        var stateJson = JsonSerializer.SerializeToElement(state, aggregateType, ExplorerJson);
        return new DcbProjectedState(projectionName, events[^1].Sequence, stateJson, events.Count);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Rehydrates an aggregate to a specific stream version using Marten's existing
    /// <c>AggregateStreamAsync</c> API. Identity may be a <see cref="Guid"/>, a string
    /// stream key, or a strongly-typed identifier — the override dispatches on
    /// <see cref="EventGraph.StreamIdentity"/>.
    /// </remarks>
    async Task<AggregateAtVersion<TAggregate>> IEventStore.RehydrateAtVersionAsync<TAggregate>(
        object identity, long version, CancellationToken ct) where TAggregate : class
    {
        await using var session = QuerySession();
        TAggregate? state;
        if (Options.EventGraph.StreamIdentity == StreamIdentity.AsGuid)
        {
            var streamId = identity is Guid g ? g : Guid.Parse(identity.ToString()!);
            state = await session.Events.AggregateStreamAsync<TAggregate>(streamId, version, token: ct).ConfigureAwait(false);
        }
        else
        {
            var streamKey = identity.ToString()!;
            state = await session.Events.AggregateStreamAsync<TAggregate>(streamKey, version, token: ct).ConfigureAwait(false);
        }

        return new AggregateAtVersion<TAggregate>(state, version, version);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Looks up the projection by aggregate-type name (matches CLR <see cref="Type.FullName"/>
    /// or short <see cref="Type.Name"/>), dispatches to the strong-typed rehydration path
    /// via reflection, and returns the result as JSON so consumers don't need the
    /// aggregate's CLR type. Returns <see langword="null"/> when no matching aggregate
    /// type is registered.
    /// </remarks>
    async Task<AggregateAtVersion?> IEventStore.RehydrateAtVersionByNameAsync(
        string aggregateTypeName, object identity, long version, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(aggregateTypeName)) throw new ArgumentNullException(nameof(aggregateTypeName));

        var aggregateType = findAggregateTypeByName(aggregateTypeName);
        if (aggregateType == null) return null;

        await using var session = QuerySession();
        var aggregate = await invokeAggregateStreamAsync(aggregateType, session, identity, version, ct).ConfigureAwait(false);
        if (aggregate == null)
        {
            return new AggregateAtVersion(aggregateType.FullNameInCode(), default, version, 0);
        }

        var stateJson = JsonSerializer.SerializeToElement(aggregate, aggregateType, ExplorerJson);
        return new AggregateAtVersion(aggregateType.FullNameInCode(), stateJson, version, version);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Builds a per-projection snapshot from <see cref="StoreOptions.Projections"/>: each
    /// registered projection contributes one <see cref="ProjectionStatus"/>, and each
    /// shard contributes a <see cref="ShardStatus"/> populated from
    /// <c>mt_event_progression</c>. The current event-store head sequence is read once
    /// up-front so every shard reports the same reference point. Live state (<c>"Running"</c>,
    /// <c>"Paused"</c>, ...) is not snapshotted here — operators get that via the existing
    /// <c>ShardStatesChanged</c> event; this method always reports <c>"Unknown"</c>.
    /// </remarks>
    async Task<IReadOnlyList<ProjectionStatus>> IEventStore.GetProjectionStatusesAsync(CancellationToken ct)
    {
        return await ((IEventStore)this).GetProjectionStatusesAsync(tenantId: null, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// #4596 Phase 1 Session 4 — override the jasperfx#407 default-throwing
    /// per-tenant overload. Non-null <paramref name="tenantId"/> returns the
    /// per-tenant slice of each registered projection: the shard identities
    /// reported carry the trailing tenant suffix
    /// (<c>{ProjName}:{ShardKey}:{tenantId}</c>) and their ProcessedSequence
    /// comes from the matching tenant-bearing row in <c>mt_event_progression</c>.
    /// Null preserves the today's-behavior "every registered shard, no tenant
    /// suffix" semantics.
    /// </summary>
    async Task<IReadOnlyList<ProjectionStatus>> IEventStore.GetProjectionStatusesAsync(string? tenantId, CancellationToken ct)
    {
        await using var session = openExplorerSession();
        await session.Database.EnsureStorageExistsAsync(typeof(IEvent), ct).ConfigureAwait(false);

        var schema = Options.EventGraph.DatabaseSchemaName;
        var headSequence = await readHeadSequenceAsync(session, schema, ct).ConfigureAwait(false);
        var progression = await readProgressionAsync(session, schema, ct).ConfigureAwait(false);

        var statuses = new List<ProjectionStatus>();
        foreach (var source in Options.Projections.All)
        {
            var shards = source.Shards();
            var shardStatuses = new List<ShardStatus>(shards.Count);
            foreach (var shard in shards)
            {
                // For per-tenant requests, compose the tenant-bearing ShardName
                // so the reported identity AND the progression-row lookup both
                // carry the trailing :tenantId suffix that Phase 1 Session 3
                // writes for per-tenant shards.
                var effectiveName = tenantId == null
                    ? shard.Name
                    : ShardName.Compose(shard.Name.Name, shard.Name.ShardKey, tenantId, shard.Name.Version);

                var processed = progression.TryGetValue(effectiveName.Identity, out var seq) ? seq : 0L;
                shardStatuses.Add(new ShardStatus(
                    effectiveName.Identity,
                    State: "Unknown",
                    ProcessedSequence: processed,
                    EventStoreSequence: headSequence,
                    Error: null));
            }

            statuses.Add(new ProjectionStatus(source.Name, source.Lifecycle.ToString(), shardStatuses));
        }

        return statuses;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Strong-typed projection step-through: feeds <paramref name="events"/> into the
    /// named projection one at a time and captures Before / After state plus elapsed time
    /// at each step. Per-step exceptions are caught and surfaced on
    /// <see cref="ProjectionStepResult{TState}.Error"/> — the timeline continues so the
    /// UI can show "this event broke this projection." Stateless: nothing is persisted.
    /// </remarks>
    async Task<ProjectionTimeline<TState>> IEventStore.RunProjectionAsync<TState>(
        string projectionName,
        object identity,
        IReadOnlyList<EventRecord> events,
        TState? startingState,
        CancellationToken ct)
        where TState : default
    {
        var aggregateType = findProjectionAggregateType(projectionName);
        if (aggregateType != typeof(TState))
        {
            throw new ArgumentException(
                $"Projection '{projectionName}' is over '{aggregateType.FullNameInCode()}', not '{typeof(TState).FullNameInCode()}'.",
                nameof(projectionName));
        }

        var rawSteps = await runProjectionStepsAsync(aggregateType, startingState, events, ct).ConfigureAwait(false);

        var typedSteps = new List<ProjectionStepResult<TState>>(rawSteps.Count);
        foreach (var s in rawSteps)
        {
            typedSteps.Add(new ProjectionStepResult<TState>(s.Event, castToTState<TState>(s.Before), castToTState<TState>(s.After), s.Elapsed, s.Error));
        }

        var finalState = rawSteps.LastOrDefault(x => x.Error == null)?.After ?? rawSteps.LastOrDefault()?.After;
        return new ProjectionTimeline<TState>(typedSteps, castToTState<TState>(finalState));
    }

    private static TState? castToTState<TState>(object? value)
    {
        if (value is null) return default;
        return (TState)value;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Untyped projection step-through: looks up the projection by name, resolves its
    /// aggregate CLR type from <see cref="IAggregateProjection.AggregateType"/>, runs the
    /// events through it, and serializes each per-step state snapshot to
    /// <see cref="JsonElement"/>. The error is reduced to its message so the record can
    /// round-trip over the wire.
    /// </remarks>
    async Task<ProjectionTimelineRaw> IEventStore.RunProjectionByNameAsync(
        string projectionName,
        object identity,
        IReadOnlyList<EventRecord> events,
        JsonElement? startingState,
        CancellationToken ct)
    {
        var aggregateType = findProjectionAggregateType(projectionName);
        var startObj = startingState?.Deserialize(aggregateType, ExplorerJson);

        var rawSteps = await runProjectionStepsAsync(aggregateType, startObj, events, ct).ConfigureAwait(false);

        var stepRecords = new List<ProjectionStepResultRaw>(rawSteps.Count);
        foreach (var s in rawSteps)
        {
            JsonElement? beforeJson = s.Before == null ? null : JsonSerializer.SerializeToElement(s.Before, aggregateType, ExplorerJson);
            JsonElement? afterJson = s.After == null ? null : JsonSerializer.SerializeToElement(s.After, aggregateType, ExplorerJson);
            stepRecords.Add(new ProjectionStepResultRaw(s.Event, beforeJson, afterJson, s.Elapsed, s.Error?.Message));
        }

        var finalObj = rawSteps.LastOrDefault(x => x.Error == null)?.After ?? rawSteps.LastOrDefault()?.After;
        JsonElement? finalJson = finalObj == null ? null : JsonSerializer.SerializeToElement(finalObj, aggregateType, ExplorerJson);

        return new ProjectionTimelineRaw(stepRecords, finalJson);
    }

    private Type findProjectionAggregateType(string projectionName)
    {
        var source = Options.Projections.All.FirstOrDefault(x => x.Name.EqualsIgnoreCase(projectionName))
            ?? throw new ArgumentOutOfRangeException(nameof(projectionName),
                $"No projection named '{projectionName}' is registered. Available projections: {Options.Projections.All.Select(p => p.Name).Join(", ")}");

        if (source is not IAggregateProjection aggregateProjection)
        {
            throw new ArgumentException(
                $"Projection '{projectionName}' is not an aggregate projection.",
                nameof(projectionName));
        }

        return aggregateProjection.AggregateType;
    }

    /// <summary>
    /// Replay events through a projection's Apply methods one event at a time using
    /// reflection. Stateless: no database session is opened, no persistence happens.
    /// Per-step exceptions are caught and surfaced on the step result so the caller's
    /// timeline can show "this event broke this projection." Suitable for the
    /// explorer's stepper view; not for projections that depend on session lookups
    /// inside Apply (those need the full aggregator machinery).
    /// </summary>
    private Task<IReadOnlyList<ObjectStepResult>> runProjectionStepsAsync(
        Type aggregateType, object? startingState, IReadOnlyList<EventRecord> events, CancellationToken ct)
    {
        var steps = new List<ObjectStepResult>(events.Count);
        var current = startingState;

        foreach (var record in events)
        {
            ct.ThrowIfCancellationRequested();
            var before = cloneViaJson(current, aggregateType);
            var sw = Stopwatch.StartNew();
            object? after;
            Exception? error = null;
            try
            {
                var domainEventData = deserializeEventBody(record);
                current = applyEventToAggregate(aggregateType, current, domainEventData);
                after = cloneViaJson(current, aggregateType);
            }
            catch (Exception ex)
            {
                error = ex;
                after = before;
            }
            sw.Stop();
            steps.Add(new ObjectStepResult(record, before, after, sw.Elapsed, error));
        }

        return Task.FromResult<IReadOnlyList<ObjectStepResult>>(steps);
    }

    private async Task<(object? state, long count)> replayAggregatorAsync(
        Type aggregateType, object? startingState, IReadOnlyList<EventRecord> records, CancellationToken ct)
    {
        if (records.Count == 0) return (startingState, 0);

        var current = startingState;
        foreach (var record in records)
        {
            ct.ThrowIfCancellationRequested();
            var data = deserializeEventBody(record);
            current = applyEventToAggregate(aggregateType, current, data);
        }
        return await Task.FromResult((current, (long)records.Count)).ConfigureAwait(false);
    }

    private object? applyEventToAggregate(Type aggregateType, object? state, object eventData)
    {
        state ??= Activator.CreateInstance(aggregateType)
            ?? throw new InvalidOperationException(
                $"Cannot construct projection state of type '{aggregateType.FullNameInCode()}' — no parameterless constructor.");

        var eventDataType = eventData.GetType();
        // Direct Apply(TEvent) — the canonical Marten projection shape.
        var applyMethod = aggregateType.GetMethod("Apply", new[] { eventDataType });
        if (applyMethod != null)
        {
            try { applyMethod.Invoke(state, new[] { eventData }); }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                throw tie.InnerException;
            }
            return state;
        }

        // Fallback: Apply(IEvent<TEvent>) — shape used by projections that need event metadata.
        var ieventOfT = typeof(IEvent<>).MakeGenericType(eventDataType);
        var wrappedApply = aggregateType.GetMethod("Apply", new[] { ieventOfT });
        if (wrappedApply != null)
        {
            var eventCtor = typeof(Event<>).MakeGenericType(eventDataType).GetConstructor(new[] { eventDataType })!;
            var wrapped = eventCtor.Invoke(new[] { eventData });
            try { wrappedApply.Invoke(state, new[] { wrapped }); }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                throw tie.InnerException;
            }
            return state;
        }

        // No Apply method matched — silently skip. The step records this by reporting
        // the event but leaving state unchanged (no error).
        return state;
    }

    private object deserializeEventBody(EventRecord record)
    {
        var mapping = Options.EventGraph.AllEvents().FirstOrDefault(m => m.EventTypeName.EqualsIgnoreCase(record.EventTypeName))
            ?? throw new InvalidOperationException(
                $"Event type '{record.EventTypeName}' is not registered. Add it via StoreOptions.Events.AddEventType.");

        var dataObj = record.Data.Deserialize(mapping.DocumentType, ExplorerJson)
            ?? throw new InvalidOperationException(
                $"Failed to deserialize event of type '{record.EventTypeName}' from supplied JSON.");
        return dataObj;
    }

    private static object? cloneViaJson(object? value, Type type)
    {
        if (value is null) return null;
        var json = JsonSerializer.SerializeToElement(value, type, ExplorerJson);
        return json.Deserialize(type, ExplorerJson);
    }

    private async Task<object?> invokeAggregateStreamAsync(
        Type aggregateType, IQuerySession session, object identity, long version, CancellationToken ct)
    {
        var streamIdentity = Options.EventGraph.StreamIdentity;
        var idArgType = streamIdentity == StreamIdentity.AsGuid ? typeof(Guid) : typeof(string);

        var aggregateMethod = typeof(JasperFx.Events.IQueryEventStore).GetMethods()
            .FirstOrDefault(m =>
                m.Name == nameof(JasperFx.Events.IQueryEventStore.AggregateStreamAsync) &&
                m.IsGenericMethodDefinition &&
                m.GetParameters().Length >= 1 &&
                m.GetParameters()[0].ParameterType == idArgType);

        if (aggregateMethod == null) return null;
        var closed = aggregateMethod.MakeGenericMethod(aggregateType);

        object idValue;
        if (streamIdentity == StreamIdentity.AsGuid)
        {
            idValue = identity is Guid g ? g : Guid.Parse(identity.ToString()!);
        }
        else
        {
            idValue = identity.ToString()!;
        }

        var paramInfos = closed.GetParameters();
        var args = new object?[paramInfos.Length];
        args[0] = idValue;
        for (var i = 1; i < paramInfos.Length; i++)
        {
            args[i] = paramInfos[i].Name switch
            {
                "version" => version,
                "token" => ct,
                _ => paramInfos[i].HasDefaultValue ? paramInfos[i].DefaultValue : null
            };
        }

        var task = (Task)closed.Invoke(session.Events, args)!;
        await task.ConfigureAwait(false);
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }

    private Type? findAggregateTypeByName(string aggregateTypeName)
    {
        foreach (var projection in Options.Projections.All.OfType<IAggregateProjection>())
        {
            var t = projection.AggregateType;
            if (t.FullName == aggregateTypeName || t.Name == aggregateTypeName)
            {
                return t;
            }
        }
        return null;
    }

    private DocumentSessionBase openExplorerSession()
    {
        var sessionOptions = new SessionOptions
        {
            AllowAnyTenant = true,
            Tracking = DocumentTracking.None
        };
        return (DocumentSessionBase)LightweightSession(sessionOptions);
    }

    private object parseStreamId(string streamId)
    {
        if (Options.EventGraph.StreamIdentity == StreamIdentity.AsGuid)
        {
            return Guid.Parse(streamId);
        }
        return streamId;
    }

    private static string readStreamIdAsString(System.Data.Common.DbDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        return value switch
        {
            Guid g => g.ToString(),
            string s => s,
            _ => value.ToString() ?? string.Empty
        };
    }

    /// <summary>
    /// Number of columns in <see cref="IEventStorage.StreamStateSelectSql"/>'s select
    /// list. Any caller that appends explorer-specific columns to that SELECT addresses
    /// the trailing ordinals as <c>StreamStateColumnCount + n</c> so the two stay in
    /// lockstep without each call site having to count.
    /// </summary>
    private const int StreamStateColumnCount = 6;

    /// <summary>
    /// Read the <see cref="IEventStorage.StreamStateSelectSql"/>-shaped columns out of
    /// the reader and project them into the explorer's diagnostic-row shape. We don't
    /// route through <see cref="Marten.Linq.Selectors.ISelector{StreamState}"/> here
    /// because <see cref="EventGraph.AggregateTypeFor"/> throws on unknown aggregate
    /// names — fine for the production read path, fatal for an explorer that has to
    /// surface legacy / unregistered streams as data.
    /// </summary>
    private static async Task<(string StreamId, string? StreamType, long Version, DateTimeOffset Created, DateTimeOffset LastUpdated, bool IsArchived)>
        readStreamRowAsync(DbDataReader reader, CancellationToken ct)
    {
        var streamId = readStreamIdAsString(reader, 0);
        var version = await reader.IsDBNullAsync(1, ct).ConfigureAwait(false) ? 0L : reader.GetInt64(1);
        var streamType = await reader.IsDBNullAsync(2, ct).ConfigureAwait(false) ? null : reader.GetString(2);
        var lastUpdated = await reader.GetFieldValueAsync<DateTimeOffset>(3, ct).ConfigureAwait(false);
        var created = await reader.GetFieldValueAsync<DateTimeOffset>(4, ct).ConfigureAwait(false);
        var isArchived = !await reader.IsDBNullAsync(5, ct).ConfigureAwait(false) && reader.GetBoolean(5);
        return (streamId, streamType, version, created, lastUpdated, isArchived);
    }

    /// <summary>
    /// Read a single mt_events row out of an explorer command into the explorer's
    /// untyped <see cref="EventRecord"/>. Both <c>ReadStreamAsync</c> and
    /// <c>QueryByTagsAsync</c> use this — they project the same eight columns
    /// (<c>id, seq_id, version, stream_id, type, data, timestamp, tenant_id</c>) into
    /// the same record shape. We don't route through
    /// <see cref="Marten.Linq.Selectors.ISelector{IEvent}"/> here because the explorer
    /// surfaces the event body as raw JSON for UI / tooling consumption rather than
    /// CLR-deserialized; the typed selector would round-trip through deserialization
    /// and re-serialization and would also break for events whose CLR type isn't
    /// registered with the EventGraph.
    /// </summary>
    private static async Task<EventRecord> readEventRecordAsync(DbDataReader reader, CancellationToken ct)
    {
        var eventId = reader.GetGuid(0);
        var sequence = reader.GetInt64(1);
        var version = reader.GetInt64(2);
        var streamIdValue = readStreamIdAsString(reader, 3);
        var typeName = reader.GetString(4);
        var dataText = reader.GetString(5);
        var timestamp = await reader.GetFieldValueAsync<DateTimeOffset>(6, ct).ConfigureAwait(false);
        var tenantId = await reader.IsDBNullAsync(7, ct).ConfigureAwait(false) ? null : reader.GetString(7);

        using var doc = JsonDocument.Parse(dataText);
        return new EventRecord(
            eventId,
            sequence,
            version,
            streamIdValue,
            typeName,
            doc.RootElement.Clone(),
            Metadata: null,
            timestamp,
            tenantId,
            Tags: null);
    }

    private static async Task<long> readHeadSequenceAsync(DocumentSessionBase session, string schema, CancellationToken ct)
    {
        var cmd = new NpgsqlCommand($"select coalesce(max(seq_id), 0) from {schema}.mt_events");
        await using var reader = await session.ExecuteReaderAsync(cmd, ct).ConfigureAwait(false);
        if (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return await reader.IsDBNullAsync(0, ct).ConfigureAwait(false) ? 0L : reader.GetInt64(0);
        }
        return 0L;
    }

    private static async Task<Dictionary<string, long>> readProgressionAsync(DocumentSessionBase session, string schema, CancellationToken ct)
    {
        var cmd = new NpgsqlCommand($"select name, last_seq_id from {schema}.mt_event_progression");
        var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await session.ExecuteReaderAsync(cmd, ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var name = reader.GetString(0);
            var seq = await reader.IsDBNullAsync(1, ct).ConfigureAwait(false) ? 0L : reader.GetInt64(1);
            map[name] = seq;
        }
        return map;
    }

    private sealed record ObjectStepResult(EventRecord Event, object? Before, object? After, TimeSpan Elapsed, Exception? Error);
}
