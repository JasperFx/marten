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
        var sql = $"select nextval('{sequenceName}') from generate_series(1,{count})";
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync(cancellation).ConfigureAwait(false);

        var queue = new Queue<long>(count);
        while (await reader.ReadAsync(cancellation).ConfigureAwait(false))
        {
            queue.Enqueue(reader.GetInt64(0));
        }

        return queue;
    }

    private void assignVersionsAndSequences(IReadOnlyList<StreamAction> streams, Queue<long> sequences)
    {
        foreach (var stream in streams)
        {
            long version = 0;
            foreach (var e in stream.Events)
            {
                version++;
                e.Version = version;
                e.Sequence = sequences.Dequeue();

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
                    await writeEventRow(writer, stream, e, cancellation).ConfigureAwait(false);
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

    private async Task writeEventRow(NpgsqlBinaryImporter writer, StreamAction stream, IEvent e,
        CancellationToken cancellation)
    {
        // seq_id
        await writer.WriteAsync(e.Sequence, NpgsqlDbType.Bigint, cancellation).ConfigureAwait(false);

        // id
        await writer.WriteAsync(e.Id, NpgsqlDbType.Uuid, cancellation).ConfigureAwait(false);

        // stream_id
        if (_events.StreamIdentity == StreamIdentity.AsGuid)
        {
            await writer.WriteAsync(stream.Id, NpgsqlDbType.Uuid, cancellation).ConfigureAwait(false);
        }
        else
        {
            await writer.WriteAsync(stream.Key!, NpgsqlDbType.Varchar, cancellation).ConfigureAwait(false);
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
        await writer.WriteAsync(e.TenantId ?? stream.TenantId ?? StorageConstants.DefaultTenantId,
            NpgsqlDbType.Varchar, cancellation).ConfigureAwait(false);

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
