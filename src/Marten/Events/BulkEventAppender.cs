using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
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

        // Step 1: Pre-allocate sequence numbers
        var sequences = await fetchSequences(conn, schema, totalEvents, cancellation).ConfigureAwait(false);

        // Step 2: Assign versions and sequences to all events
        assignVersionsAndSequences(streams, sequences);

        // Step 3: COPY streams
        await copyStreams(conn, schema, streams, batchSize, cancellation).ConfigureAwait(false);

        // Step 4: COPY events
        await copyEvents(conn, schema, streams, batchSize, cancellation).ConfigureAwait(false);

        // Step 5: Update high water mark
        await updateHighWaterMark(conn, schema, streams, cancellation).ConfigureAwait(false);
    }

    private async Task<Queue<long>> fetchSequences(
        NpgsqlConnection conn,
        string schema,
        int count,
        CancellationToken cancellation)
    {
        var sql = $"select nextval('{schema}.mt_events_sequence') from generate_series(1,{count})";
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

        if (stream.AggregateTypeName != null)
        {
            await writer.WriteAsync(stream.AggregateTypeName, NpgsqlDbType.Varchar, cancellation)
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

        // data (jsonb or bytea)
        if (_events.UseMemoryPackSerialization && _events.BinarySerializer != null)
        {
            var bytes = _events.BinarySerializer.Serialize(e.EventType, e.Data);
            await writer.WriteAsync(bytes, NpgsqlDbType.Bytea, cancellation).ConfigureAwait(false);
        }
        else
        {
            var json = _serializer.ToJson(e.Data);
            await writer.WriteAsync(json, NpgsqlDbType.Jsonb, cancellation).ConfigureAwait(false);
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
                var headersJson = _serializer.ToJson(e.Headers);
                await writer.WriteAsync(headersJson, NpgsqlDbType.Jsonb, cancellation).ConfigureAwait(false);
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

        var sql = $@"
INSERT INTO {schema}.mt_event_progression (name, last_seq_id)
VALUES ('HighWaterMark', @seq)
ON CONFLICT (name) DO UPDATE SET last_seq_id = GREATEST({schema}.mt_event_progression.last_seq_id, @seq)";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("seq", maxSequence);
        await cmd.ExecuteNonQueryAsync(cancellation).ConfigureAwait(false);
    }
}
