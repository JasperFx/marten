using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Marten.Events.Daemon.HighWater;
using Marten.Events.Schema;
using Marten.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Events;

/// <summary>
///     Uses PostgreSQL COPY binary import to efficiently bulk-load large numbers of events.
///     This bypasses the normal append pipeline (no inline projections, no optimistic concurrency)
///     and is intended for seeding, migration, and load testing scenarios.
/// </summary>
internal class BulkEventAppender
{
    private readonly EventGraph _events;
    private readonly ISerializer _serializer;

    public BulkEventAppender(EventGraph events, ISerializer serializer)
    {
        _events = events;
        _serializer = serializer;
    }

    public async Task BulkInsertAsync(
        NpgsqlConnection conn,
        IReadOnlyList<StreamAction> streams,
        int batchSize,
        CancellationToken cancellation)
    {
        if (streams.Count == 0) return;

        var totalEvents = streams.Sum(s => s.Events.Count);
        if (totalEvents == 0) return;

        var schema = _events.DatabaseSchemaName;

        // Steps 1+2: pre-allocate sequence numbers and assign them (with versions) to all events. Under
        // per-tenant event partitioning each tenant MUST draw from its own mt_events_sequence_{suffix} —
        // the same sequence the quick-append function uses for live appends. Drawing from the store-global
        // sequence would leave every imported tenant's own sequence at 1, so the tenant's first live
        // append re-issues an already-used seq_id (PK collision on its events partition).
        if (_events.UseTenantPartitionedEvents)
        {
            foreach (var tenantGroup in streams.GroupBy(s => s.TenantId))
            {
                var tenantStreams = tenantGroup.ToList();
                var tenantEventCount = tenantStreams.Sum(s => s.Events.Count);
                if (tenantEventCount == 0) continue;

                var sequenceName = await resolveSequenceNameAsync(conn, schema, tenantGroup.Key, cancellation)
                    .ConfigureAwait(false);
                var tenantSequences = await fetchSequences(conn, sequenceName, tenantEventCount, cancellation)
                    .ConfigureAwait(false);
                assignVersionsAndSequences(tenantStreams, tenantSequences);
            }
        }
        else
        {
            var sequences = await fetchSequences(conn, $"{schema}.mt_events_sequence", totalEvents, cancellation)
                .ConfigureAwait(false);
            assignVersionsAndSequences(streams, sequences);
        }

        // Step 3: COPY streams
        await copyStreams(conn, schema, streams, batchSize, cancellation).ConfigureAwait(false);

        // Step 4: COPY events
        await copyEvents(conn, schema, streams, batchSize, cancellation).ConfigureAwait(false);

        // Step 5: Update high water mark
        await updateHighWaterMark(conn, schema, streams, cancellation).ConfigureAwait(false);
    }

    /// <summary>
    /// Streaming, order-preserving bulk import for a single tenant — see
    /// <see cref="Marten.DocumentStore.BulkInsertEventStreamAsync" />. Consumes <paramref name="orderedEvents" />
    /// lazily in batches (bounded memory), assigning each event the next ascending sequence in arrival order
    /// (so cross-stream ordering is preserved) and taking per-stream versions from the events themselves.
    /// <paramref name="streamHeaders" /> seed mt_streams first so the mt_events foreign key holds.
    /// Under <see cref="BulkEventSequenceMode.PreserveSourceSequence"/> the events keep the Sequence they
    /// already carry (a migration never renumbers history — see marten#4682's data policy) and the target
    /// sequence is advanced past the imported maximum via setval instead of being drawn from.
    /// </summary>
    public async Task BulkInsertEventStreamAsync(
        NpgsqlConnection conn,
        string tenantId,
        IReadOnlyList<BulkEventStreamHeader> streamHeaders,
        IAsyncEnumerable<IEvent> orderedEvents,
        BulkEventSequenceMode sequenceMode,
        int batchSize,
        CancellationToken cancellation)
    {
        var schema = _events.DatabaseSchemaName;

        // Resolve the sequence to draw seq_ids from BEFORE any COPY opens: under per-tenant event
        // partitioning this is the tenant's own mt_events_sequence_{suffix} (the same one live
        // quick-appends use), otherwise the store-global sequence. See BulkInsertAsync for why drawing
        // from the wrong sequence corrupts the tenant's post-import appends.
        var sequenceName = await resolveSequenceNameAsync(conn, schema, tenantId, cancellation)
            .ConfigureAwait(false);

        // mt_streams first — it is the foreign-key target for mt_events. Bounded: one row per stream.
        await copyStreamHeaders(conn, schema, tenantId, streamHeaders, batchSize, cancellation)
            .ConfigureAwait(false);

        var columns = buildEventColumns();
        var copySql = $"COPY {schema}.mt_events({string.Join(", ", columns.Select(c => $"\"{c}\""))}) FROM STDIN BINARY";

        // Sequences are pulled one block (batchSize) at a time — not the whole tenant up front — so memory
        // stays bounded when the total event count is unknown. The COPY writer is (re)opened per block, and a
        // block boundary is the ONLY place the nextval SELECT runs. That matters: a regular command cannot run
        // on a connection that is mid-COPY (Npgsql throws "connection is already in state 'Copy'"), so the
        // writer is always CLOSED before refillSequences and reopened afterwards. Same single connection, so
        // no extra connection is taken from a tightly pooled shard database. In PreserveSourceSequence mode
        // there are no mid-stream sequence SELECTs at all — one COPY spans the whole import and the target
        // sequence is advanced once at the end.
        var preserveSequences = sequenceMode == BulkEventSequenceMode.PreserveSourceSequence;
        var sequences = new Queue<long>(batchSize);
        long maxSequence = 0;
        var count = 0;
        NpgsqlBinaryImporter? writer = null;

        try
        {
            await foreach (var e in orderedEvents.WithCancellation(cancellation).ConfigureAwait(false))
            {
                long seq;
                if (preserveSequences)
                {
                    // Migration mode: the event keeps the seq_id it already carries. Strictly ascending
                    // arrival order is REQUIRED — it both guarantees the preserved cross-stream ordering
                    // and catches a caller feeding unsorted (or unnumbered) source events, which would
                    // otherwise silently corrupt the tenant's replay order. maxSequence starts at 0, so
                    // the first event's Sequence must be positive.
                    seq = e.Sequence;
                    if (seq <= maxSequence)
                    {
                        throw new InvalidOperationException(
                            $"{nameof(BulkEventSequenceMode.PreserveSourceSequence)} requires the incoming events " +
                            $"to carry strictly ascending, positive Sequence values, but got {seq} after {maxSequence}. " +
                            "Supply the source events ordered by their original seq_id.");
                    }
                }
                else
                {
                    if (sequences.Count == 0)
                    {
                        // Close the open COPY before the sequence SELECT, then start a fresh one for the next block.
                        if (writer != null)
                        {
                            await writer.CompleteAsync(cancellation).ConfigureAwait(false);
                            await writer.DisposeAsync().ConfigureAwait(false);
                            writer = null;
                        }

                        await refillSequences(conn, sequenceName, sequences, batchSize, cancellation).ConfigureAwait(false);
                    }

                    seq = sequences.Dequeue();
                }

                writer ??= await conn.BeginBinaryImportAsync(copySql, cancellation).ConfigureAwait(false);

                e.Sequence = seq;
                if (seq > maxSequence)
                {
                    maxSequence = seq;
                }

                // Force-stamp the tenant id on every event, mirroring the batch overload — the rows are
                // written into THIS tenant's partition, so a stale TenantId carried over from the source
                // store must never leak through.
                e.TenantId = tenantId;

                // Ensure event type metadata is populated (a migration may clear it so the current alias is
                // re-derived from the upcasted CLR type).
                if (string.IsNullOrEmpty(e.EventTypeName))
                {
                    var mapping = _events.EventMappingFor(e.EventType);
                    e.EventTypeName = mapping.EventTypeName;
                    e.DotNetTypeName = mapping.DotNetTypeName;
                }

                await writer.StartRowAsync(cancellation).ConfigureAwait(false);
                await writeEventRow(writer, e, e.StreamId, e.StreamKey, tenantId, cancellation)
                    .ConfigureAwait(false);
                count++;
            }

            if (writer != null)
            {
                await writer.CompleteAsync(cancellation).ConfigureAwait(false);
            }
        }
        finally
        {
            if (writer != null)
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }
        }

        if (count == 0)
        {
            return;
        }

        if (preserveSequences)
        {
            // The imported seq_ids were NOT drawn from the sequence, so advance it past the imported
            // maximum — otherwise the tenant's first live append re-issues an already-used seq_id
            // (PK collision on its events partition). GREATEST keeps an already-further-along sequence
            // untouched.
            await advanceSequencePastAsync(conn, sequenceName, maxSequence, cancellation).ConfigureAwait(false);
        }

        // High water: under per-tenant event partitioning, AssignFromSequence deliberately writes NOTHING —
        // since #4847 the per-tenant high-water detection derives each tenant's mark from max(seq_id) of its
        // own events, and advancing the store-global row with a tenant-local seq would misrepresent one
        // tenant's height as the whole store's. PreserveSourceSequence DOES seed the tenant's own
        // HighWaterMark:{tenant} progression row (marten#4682 Phase 2 step 8): a migrated tenant's seq_ids
        // are extremely gappy (the conjoined source interleaved tenants on one global sequence), and without
        // a persisted mark above that history the detector's gap-walk (#4867) has to grind through every
        // hole before the tenant's projections can start. Gaps BELOW a persisted mark are never revisited.
        // Non-partitioned stores keep the store-global upsert in both modes, mirroring the batch overload.
        if (_events.UseTenantPartitionedEvents)
        {
            if (preserveSequences)
            {
                await upsertPerTenantHighWaterMark(conn, schema, tenantId, maxSequence, cancellation)
                    .ConfigureAwait(false);
            }
        }
        else
        {
            await upsertStoreGlobalHighWaterMark(conn, schema, maxSequence, cancellation).ConfigureAwait(false);
        }
    }

    private async Task copyStreamHeaders(
        NpgsqlConnection conn,
        string schema,
        string tenantId,
        IReadOnlyList<BulkEventStreamHeader> headers,
        int batchSize,
        CancellationToken cancellation)
    {
        if (headers.Count == 0)
        {
            return;
        }

        var columns = buildStreamColumns();
        var copySql = $"COPY {schema}.mt_streams({string.Join(", ", columns.Select(c => $"\"{c}\""))}) FROM STDIN BINARY";

        var batch = 0;
        NpgsqlBinaryImporter? writer = null;

        try
        {
            foreach (var header in headers)
            {
                if (batch % batchSize == 0)
                {
                    if (writer != null)
                    {
                        await writer.CompleteAsync(cancellation).ConfigureAwait(false);
                        await writer.DisposeAsync().ConfigureAwait(false);
                    }

                    writer = await conn.BeginBinaryImportAsync(copySql, cancellation).ConfigureAwait(false);
                }

                await writer!.StartRowAsync(cancellation).ConfigureAwait(false);

                if (_events.TenancyStyle == TenancyStyle.Conjoined)
                {
                    await writer.WriteAsync(tenantId, NpgsqlDbType.Varchar, cancellation).ConfigureAwait(false);
                }

                if (_events.StreamIdentity == StreamIdentity.AsGuid)
                {
                    await writer.WriteAsync(header.Id, NpgsqlDbType.Uuid, cancellation).ConfigureAwait(false);
                }
                else
                {
                    await writer.WriteAsync(header.Key!, NpgsqlDbType.Varchar, cancellation).ConfigureAwait(false);
                }

                // Same alias derivation as the batch path (#4625): honor a pre-resolved alias first,
                // otherwise derive it from the aggregate CLR type.
                var aggregateTypeName = header.AggregateTypeName
                                        ?? (header.AggregateType != null
                                            ? _events.AggregateAliasFor(header.AggregateType)
                                            : null);
                if (aggregateTypeName != null)
                {
                    await writer.WriteAsync(aggregateTypeName, NpgsqlDbType.Varchar, cancellation)
                        .ConfigureAwait(false);
                }
                else
                {
                    await writer.WriteNullAsync(cancellation).ConfigureAwait(false);
                }

                await writer.WriteAsync(header.Version, NpgsqlDbType.Bigint, cancellation).ConfigureAwait(false);
                await writer.WriteAsync(header.IsArchived, NpgsqlDbType.Boolean, cancellation).ConfigureAwait(false);

                batch++;
            }

            if (writer != null)
            {
                await writer.CompleteAsync(cancellation).ConfigureAwait(false);
            }
        }
        finally
        {
            if (writer != null)
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// The (schema-qualified) sequence that seq_ids for this tenant's events must be drawn from. Under
    /// per-tenant event partitioning that is the tenant's own <c>mt_events_sequence_{suffix}</c> — the
    /// suffix resolved from the tenants partition table exactly like the quick-append function does —
    /// so the sequence's position stays consistent with the imported events and live appends continue
    /// seamlessly after an import. Otherwise the store-global <c>mt_events_sequence</c>.
    /// </summary>
    private async Task<string> resolveSequenceNameAsync(
        NpgsqlConnection conn,
        string schema,
        string? tenantId,
        CancellationToken cancellation)
    {
        if (!_events.UseTenantPartitionedEvents || tenantId == null)
        {
            return $"{schema}.mt_events_sequence";
        }

        var tenantsTable = _events.Options.TenantPartitions!.TenantsTableName;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"select partition_suffix from {tenantsTable} where partition_value = @tenant";
        cmd.Parameters.AddWithValue("tenant", tenantId);
        var suffix = await cmd.ExecuteScalarAsync(cancellation).ConfigureAwait(false) as string;

        if (suffix == null)
        {
            throw new InvalidOperationException(
                $"Tenant '{tenantId}' has no registered partition. Call AddMartenManagedTenantsAsync " +
                "(or the sharded AddTenantToShardAsync) before bulk-importing its events.");
        }

        return $"{schema}.mt_events_sequence_{suffix}";
    }

    private async Task<Queue<long>> fetchSequences(
        NpgsqlConnection conn,
        string sequenceName,
        int count,
        CancellationToken cancellation)
    {
        var queue = new Queue<long>(count);
        await refillSequences(conn, sequenceName, queue, count, cancellation).ConfigureAwait(false);
        return queue;
    }

    // Pull a block of freshly allocated sequence numbers into the queue. Used by the streaming import to
    // top up lazily (block by block) instead of pre-allocating the whole tenant's range up front.
    private static async Task refillSequences(
        NpgsqlConnection conn,
        string sequenceName,
        Queue<long> queue,
        int count,
        CancellationToken cancellation)
    {
        var sql = $"select nextval('{sequenceName}') from generate_series(1,{count})";
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync(cancellation).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellation).ConfigureAwait(false))
        {
            queue.Enqueue(reader.GetInt64(0));
        }
    }

    private void assignVersionsAndSequences(IReadOnlyList<StreamAction> streams, Queue<long> sequences)
    {
        // Version is per-stream (1..N in the stream's own event order).
        foreach (var stream in streams)
        {
            long version = 0;
            foreach (var e in stream.Events)
            {
                version++;
                e.Version = version;

                // Ensure event type metadata is populated
                if (string.IsNullOrEmpty(e.EventTypeName))
                {
                    var mapping = _events.EventMappingFor(e.EventType);
                    e.EventTypeName = mapping.EventTypeName;
                    e.DotNetTypeName = mapping.DotNetTypeName;
                }
            }

            stream.Version = version;
        }

        // seq_id is assigned GLOBALLY across all streams, honoring the order the events already carry in
        // their Sequence — so a caller that supplies events in their original global order (e.g. a migration
        // reading source events by seq_id) keeps cross-stream ordering in the target, instead of each stream
        // getting a contiguous seq_id block that flattens the interleaving. That matters for multi-stream
        // projections / subscriptions that compare Sequence across streams. LINQ OrderBy is stable, so events
        // with no meaningful pre-set Sequence (fresh seeding, all 0) keep the per-stream order the caller
        // passed — identical to the previous behavior.
        foreach (var e in streams.SelectMany(s => s.Events).OrderBy(e => e.Sequence))
        {
            e.Sequence = sequences.Dequeue();
        }
    }

    private async Task copyStreams(
        NpgsqlConnection conn,
        string schema,
        IReadOnlyList<StreamAction> streams,
        int batchSize,
        CancellationToken cancellation)
    {
        var columns = buildStreamColumns();
        var copySql = $"COPY {schema}.mt_streams({string.Join(", ", columns.Select(c => $"\"{c}\""))}) FROM STDIN BINARY";

        var batch = 0;
        NpgsqlBinaryImporter? writer = null;

        try
        {
            foreach (var stream in streams)
            {
                if (batch % batchSize == 0)
                {
                    if (writer != null)
                    {
                        await writer.CompleteAsync(cancellation).ConfigureAwait(false);
                        await writer.DisposeAsync().ConfigureAwait(false);
                    }

                    writer = await conn.BeginBinaryImportAsync(copySql, cancellation).ConfigureAwait(false);
                }

                await writer!.StartRowAsync(cancellation).ConfigureAwait(false);
                await writeStreamRow(writer, stream, cancellation).ConfigureAwait(false);
                batch++;
            }

            if (writer != null)
            {
                await writer.CompleteAsync(cancellation).ConfigureAwait(false);
            }
        }
        finally
        {
            if (writer != null)
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private List<string> buildStreamColumns()
    {
        var columns = new List<string>();

        if (_events.TenancyStyle == TenancyStyle.Conjoined)
        {
            columns.Add("tenant_id");
        }

        columns.Add("id");
        columns.Add("type");
        columns.Add("version");
        columns.Add("is_archived");

        return columns;
    }

    private async Task writeStreamRow(NpgsqlBinaryImporter writer, StreamAction stream,
        CancellationToken cancellation)
    {
        if (_events.TenancyStyle == TenancyStyle.Conjoined)
        {
            await writer.WriteAsync(stream.TenantId ?? StorageConstants.DefaultTenantId, NpgsqlDbType.Varchar,
                cancellation).ConfigureAwait(false);
        }

        if (_events.StreamIdentity == StreamIdentity.AsGuid)
        {
            await writer.WriteAsync(stream.Id, NpgsqlDbType.Uuid, cancellation).ConfigureAwait(false);
        }
        else
        {
            await writer.WriteAsync(stream.Key!, NpgsqlDbType.Varchar, cancellation).ConfigureAwait(false);
        }

        // #4625: BulkInsertEventsAsync writes mt_streams.type from
        // AggregateTypeName, but that string is only ever populated inside
        // StreamAction.PrepareEvents (which is NOT called on the bulk path —
        // PrepareEvents also dequeues sequence values and assigns
        // Sequence/Version/Timestamp, work the bulk importer does itself).
        // So a caller that does the documented thing —
        //   var action = StreamAction.Start(events, key, …);
        //   action.AggregateType = typeof(MyAggregate);
        //   await store.BulkInsertEventsAsync(tenant, new[] { action });
        // — would end up with mt_streams.type = NULL, breaking later appends
        // under UseMandatoryStreamTypeDeclaration. Derive the alias here so
        // the public API surface (AggregateType) is honored without making
        // callers reach for the internal AggregateTypeName setter.
        // AggregateTypeName has an internal set scoped to JasperFx.Events so
        // we can't assign it from Marten — compute a local fallback string
        // and write THAT instead. Future-proofing: if AggregateTypeName is
        // ever already set (e.g. caller round-tripped a fully-prepared action),
        // honor that value first.
        var aggregateTypeName = stream.AggregateTypeName
                                ?? (stream.AggregateType != null
                                    ? _events.AggregateAliasFor(stream.AggregateType)
                                    : null);

        if (aggregateTypeName != null)
        {
            await writer.WriteAsync(aggregateTypeName, NpgsqlDbType.Varchar, cancellation)
                .ConfigureAwait(false);
        }
        else
        {
            await writer.WriteNullAsync(cancellation).ConfigureAwait(false);
        }

        await writer.WriteAsync(stream.Version, NpgsqlDbType.Bigint, cancellation).ConfigureAwait(false);
        await writer.WriteAsync(false, NpgsqlDbType.Boolean, cancellation).ConfigureAwait(false);
    }

    private async Task copyEvents(
        NpgsqlConnection conn,
        string schema,
        IReadOnlyList<StreamAction> streams,
        int batchSize,
        CancellationToken cancellation)
    {
        var columns = buildEventColumns();
        var copySql = $"COPY {schema}.mt_events({string.Join(", ", columns.Select(c => $"\"{c}\""))}) FROM STDIN BINARY";

        var batch = 0;
        NpgsqlBinaryImporter? writer = null;

        try
        {
            foreach (var stream in streams)
            {
                foreach (var e in stream.Events)
                {
                    if (batch % batchSize == 0)
                    {
                        if (writer != null)
                        {
                            await writer.CompleteAsync(cancellation).ConfigureAwait(false);
                            await writer.DisposeAsync().ConfigureAwait(false);
                        }

                        writer = await conn.BeginBinaryImportAsync(copySql, cancellation).ConfigureAwait(false);
                    }

                    await writer!.StartRowAsync(cancellation).ConfigureAwait(false);
                    await writeEventRow(writer, e, stream.Id, stream.Key,
                            e.TenantId ?? stream.TenantId ?? StorageConstants.DefaultTenantId, cancellation)
                        .ConfigureAwait(false);
                    batch++;
                }
            }

            if (writer != null)
            {
                await writer.CompleteAsync(cancellation).ConfigureAwait(false);
            }
        }
        finally
        {
            if (writer != null)
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private List<string> buildEventColumns()
    {
        var columns = new List<string>
        {
            "seq_id",
            "id",
            "stream_id",
            "version",
            "data",
            // #4515 Phase 2: bdata bytea (nullable) — bytes for binary events,
            // NULL for JSON events. Always present in the COPY column list
            // because mt_events.bdata is universal; writeEventRow writes
            // either the binary payload or NULL per event.
            "bdata",
            "type",
            "timestamp",
            "tenant_id",
            "mt_dotnet_type",
            "is_archived"
        };

        if (_events.Metadata.CorrelationId.Enabled)
        {
            columns.Add("correlation_id");
        }

        if (_events.Metadata.CausationId.Enabled)
        {
            columns.Add("causation_id");
        }

        if (_events.Metadata.Headers.Enabled)
        {
            columns.Add("headers");
        }

        if (_events.Metadata.UserName.Enabled)
        {
            columns.Add("user_name");
        }

        return columns;
    }

    // Shared row writer for both bulk paths: the batch path passes the StreamAction's identity/tenant,
    // the streaming path passes the event's own stream identity and the (force-stamped) import tenant.
    private async Task writeEventRow(NpgsqlBinaryImporter writer, IEvent e, Guid streamId, string? streamKey,
        string tenantId, CancellationToken cancellation)
    {
        // seq_id
        await writer.WriteAsync(e.Sequence, NpgsqlDbType.Bigint, cancellation).ConfigureAwait(false);

        // id
        await writer.WriteAsync(e.Id, NpgsqlDbType.Uuid, cancellation).ConfigureAwait(false);

        // stream_id
        if (_events.StreamIdentity == StreamIdentity.AsGuid)
        {
            await writer.WriteAsync(streamId, NpgsqlDbType.Uuid, cancellation).ConfigureAwait(false);
        }
        else
        {
            await writer.WriteAsync(streamKey!, NpgsqlDbType.Varchar, cancellation).ConfigureAwait(false);
        }

        // version
        await writer.WriteAsync(e.Version, NpgsqlDbType.Bigint, cancellation).ConfigureAwait(false);

        // data (jsonb) — direct UTF-8 serialization, no intermediate string
        // allocation. For binary events (#4515), emits the {} placeholder so
        // the `data NOT NULL` constraint stays intact; the real payload
        // travels in the bdata bytea slot below.
        var mapping = _events.EventMappingFor(e.EventType);
        var isBinary = mapping?.IsBinary == true;

        var dataBytes = isBinary
            ? "{}"u8.ToArray()
            : SerializeToUtf8(_serializer, e.Data);
        await writer.WriteAsync(dataBytes, NpgsqlDbType.Jsonb, cancellation).ConfigureAwait(false);

        // bdata (bytea, nullable) — binary payload for [BinaryEvent] types;
        // NULL for JSON-serialized events.
        if (isBinary)
        {
            var bdataBytes = mapping!.BinarySerializer!.Serialize(e.EventType, e.Data);
            await writer.WriteAsync(bdataBytes, NpgsqlDbType.Bytea, cancellation).ConfigureAwait(false);
        }
        else
        {
            await writer.WriteNullAsync(cancellation).ConfigureAwait(false);
        }

        // type
        await writer.WriteAsync(e.EventTypeName, NpgsqlDbType.Varchar, cancellation).ConfigureAwait(false);

        // timestamp
        await writer.WriteAsync(e.Timestamp != default ? e.Timestamp : DateTimeOffset.UtcNow,
            NpgsqlDbType.TimestampTz, cancellation).ConfigureAwait(false);

        // tenant_id
        await writer.WriteAsync(tenantId, NpgsqlDbType.Varchar, cancellation).ConfigureAwait(false);

        // mt_dotnet_type
        if (!string.IsNullOrEmpty(e.DotNetTypeName))
        {
            await writer.WriteAsync(e.DotNetTypeName, NpgsqlDbType.Varchar, cancellation).ConfigureAwait(false);
        }
        else
        {
            await writer.WriteNullAsync(cancellation).ConfigureAwait(false);
        }

        // is_archived
        await writer.WriteAsync(e.IsArchived, NpgsqlDbType.Boolean, cancellation).ConfigureAwait(false);

        // Optional metadata columns
        if (_events.Metadata.CorrelationId.Enabled)
        {
            if (e.CorrelationId != null)
            {
                await writer.WriteAsync(e.CorrelationId, NpgsqlDbType.Varchar, cancellation).ConfigureAwait(false);
            }
            else
            {
                await writer.WriteNullAsync(cancellation).ConfigureAwait(false);
            }
        }

        if (_events.Metadata.CausationId.Enabled)
        {
            if (e.CausationId != null)
            {
                await writer.WriteAsync(e.CausationId, NpgsqlDbType.Varchar, cancellation).ConfigureAwait(false);
            }
            else
            {
                await writer.WriteNullAsync(cancellation).ConfigureAwait(false);
            }
        }

        if (_events.Metadata.Headers.Enabled)
        {
            if (e.Headers != null)
            {
                var headersBytes = SerializeToUtf8(_serializer, e.Headers);
                await writer.WriteAsync(headersBytes, NpgsqlDbType.Jsonb, cancellation).ConfigureAwait(false);
            }
            else
            {
                await writer.WriteNullAsync(cancellation).ConfigureAwait(false);
            }
        }

        if (_events.Metadata.UserName.Enabled)
        {
            if (e.UserName != null)
            {
                await writer.WriteAsync(e.UserName, NpgsqlDbType.Varchar, cancellation).ConfigureAwait(false);
            }
            else
            {
                await writer.WriteNullAsync(cancellation).ConfigureAwait(false);
            }
        }
    }

    private async Task updateHighWaterMark(
        NpgsqlConnection conn,
        string schema,
        IReadOnlyList<StreamAction> streams,
        CancellationToken cancellation)
    {
        var maxSequence = streams
            .SelectMany(s => s.Events)
            .Max(e => e.Sequence);

        // #4681: the literal 'HighWaterMark' name is produced by HighWaterShardIdentity so
        // any future change to the grammar lands in one place rather than in scattered SQL.
        var sql = $@"
INSERT INTO {schema}.mt_event_progression (name, last_seq_id)
VALUES ('{HighWaterShardIdentity.StoreGlobal}', @seq)
ON CONFLICT (name) DO UPDATE SET last_seq_id = GREATEST({schema}.mt_event_progression.last_seq_id, @seq)";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("seq", maxSequence);
        await cmd.ExecuteNonQueryAsync(cancellation).ConfigureAwait(false);
    }

    // PreserveSourceSequence only: push the (tenant or store-global) sequence past the imported maximum
    // so live appends continue above the preserved history. setval(..., true) makes the NEXT nextval
    // return seq + 1; GREATEST leaves a sequence that is already further along untouched.
    private static async Task advanceSequencePastAsync(
        NpgsqlConnection conn,
        string sequenceName,
        long seq,
        CancellationToken cancellation)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"select setval('{sequenceName}', greatest((select last_value from {sequenceName}), @seq), true)";
        cmd.Parameters.AddWithValue("seq", seq);
        await cmd.ExecuteNonQueryAsync(cancellation).ConfigureAwait(false);
    }

    // PreserveSourceSequence + per-tenant partitioning: seed the tenant's own high-water progression row.
    // #4681: the identity is produced by HighWaterShardIdentity, never hand-rolled SQL concatenation.
    private static async Task upsertPerTenantHighWaterMark(
        NpgsqlConnection conn,
        string schema,
        string tenantId,
        long seq,
        CancellationToken cancellation)
    {
        var sql = $@"
INSERT INTO {schema}.mt_event_progression (name, last_seq_id)
VALUES (@name, @seq)
ON CONFLICT (name) DO UPDATE SET last_seq_id = GREATEST({schema}.mt_event_progression.last_seq_id, @seq)";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("name", HighWaterShardIdentity.PerTenant(tenantId));
        cmd.Parameters.AddWithValue("seq", seq);
        await cmd.ExecuteNonQueryAsync(cancellation).ConfigureAwait(false);
    }

    // Streaming-path sibling of updateHighWaterMark: same store-global upsert, driven by the running
    // max rather than a materialized stream list.
    private static async Task upsertStoreGlobalHighWaterMark(
        NpgsqlConnection conn,
        string schema,
        long seq,
        CancellationToken cancellation)
    {
        var sql = $@"
INSERT INTO {schema}.mt_event_progression (name, last_seq_id)
VALUES ('{HighWaterShardIdentity.StoreGlobal}', @seq)
ON CONFLICT (name) DO UPDATE SET last_seq_id = GREATEST({schema}.mt_event_progression.last_seq_id, @seq)";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("seq", seq);
        await cmd.ExecuteNonQueryAsync(cancellation).ConfigureAwait(false);
    }

    /// <summary>
    /// Serialize <paramref name="value"/> to a sized UTF-8 byte[] via the serializer's
    /// buffer-writer path. Avoids the intermediate <see cref="string"/> allocation that
    /// <see cref="ISerializer.ToJson"/> produces — meaningful across long bulk imports.
    /// </summary>
    private static byte[] SerializeToUtf8(ISerializer serializer, object? value)
    {
        using var buffer = new Services.PooledByteBufferWriter();
        serializer.WriteTo(buffer, value);
        return buffer.ToSizedArray();
    }
}
