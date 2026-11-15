using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Exceptions;
using Marten.Testing.Harness;
using MemoryPack;
using Npgsql;
using Polly;
using Shouldly;
using Xunit;

namespace Marten.MemoryPack.Tests;

/// <summary>
///     #5731 / jasperfx#1187: <c>mt_events.bdata</c> is written from a pooled buffer and read through a span.
///     The batched Quick append serializes every binary event of a stream into ONE pooled buffer and binds slices
///     of it, so these pin that every slice lands on its own row: payloads of different sizes, one large enough to
///     force the buffer to grow (and move) part-way through the batch, interleaved with JSON events whose bdata
///     must stay NULL.
/// </summary>
[Collection("Marten.MemoryPack")]
public class PooledBdataTests
{
    // Bigger than PooledByteBufferWriter's initial 256-byte rental, so writing it grows the buffer after the
    // earlier binary events of the same batch were already written into it.
    private static readonly string LongRoute = string.Join(" > ", Enumerable.Range(0, 400).Select(i => $"stop-{i}"));

    private static object[] MixedEvents(Guid streamId) =>
    [
        new TripStarted(streamId, "Erin", DateTimeOffset.UtcNow), // binary, small
        new TripCommentAdded(streamId, "first", DateTimeOffset.UtcNow), // JSON
        new RouteRecorded(streamId, LongRoute), // binary, grows the pooled buffer
        new PassengerPickedUp(streamId, "Frank", DateTimeOffset.UtcNow), // binary, small, after the growth
        new TripCommentAdded(streamId, "second", DateTimeOffset.UtcNow), // JSON
        new TripEnded(streamId, DateTimeOffset.UtcNow, 42.25m) // binary
    ];

    private static async Task<DocumentStore> BuildStore(string schema, EventAppendMode mode,
        Action<StoreOptions>? configure = null)
    {
        var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = schema;
            opts.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
            opts.Events.AppendMode = mode;
            opts.Events.UseMemoryPackSerializer();
            configure?.Invoke(opts);
        });

        await store.Advanced.Clean.CompletelyRemoveAllAsync(TestContext.Current.CancellationToken);
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        return store;
    }

    [Theory]
    [InlineData(EventAppendMode.QuickWithServerTimestamps)]
    [InlineData(EventAppendMode.Quick)]
    [InlineData(EventAppendMode.Rich)]
    public async Task every_binary_event_of_a_batch_lands_on_its_own_row(EventAppendMode mode)
    {
        await using var store = await BuildStore($"memorypack_pooled_{mode.ToString().ToLowerInvariant()}", mode);

        var streamId = Guid.NewGuid();
        var appended = MixedEvents(streamId);

        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream(streamId, appended);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await assertRowsMatch(store, streamId, appended);
        await assertReadsBack(store, streamId, appended);
    }

    [Fact]
    public async Task an_append_to_an_existing_stream_slices_its_own_batch()
    {
        await using var store = await BuildStore("memorypack_pooled_append", EventAppendMode.QuickWithServerTimestamps);

        var streamId = Guid.NewGuid();
        var appended = MixedEvents(streamId);

        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream(streamId, appended.Take(2).ToArray());
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var session = store.LightweightSession())
        {
            session.Events.Append(streamId, appended.Skip(2).ToArray());
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await assertRowsMatch(store, streamId, appended);
        await assertReadsBack(store, streamId, appended);
    }

    [Fact]
    public async Task bulk_insert_writes_binary_events_from_the_pooled_buffer()
    {
        await using var store = await BuildStore("memorypack_pooled_bulk", EventAppendMode.QuickWithServerTimestamps);

        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var firstEvents = MixedEvents(first);
        var secondEvents = MixedEvents(second).Reverse().ToArray();

        await store.BulkInsertEventsAsync(
            [StreamAction.Start(store.Events, first, firstEvents), StreamAction.Start(store.Events, second, secondEvents)],
            cancellation: TestContext.Current.CancellationToken);

        await assertRowsMatch(store, first, firstEvents);
        await assertRowsMatch(store, second, secondEvents);
        await assertReadsBack(store, first, firstEvents);
        await assertReadsBack(store, second, secondEvents);
    }

    /// <summary>
    ///     The pooled buffer belongs to the operation until the whole unit of work is finished (#5262): the
    ///     resilience pipeline re-executes the very same <c>NpgsqlBatch</c> on a retry, and Npgsql only copies
    ///     the parameter bytes when the batch executes. The first attempt here blows the command timeout, and
    ///     the retry callback churns the shared byte pool, so a buffer that had gone back to the pool after the
    ///     first attempt would be sent with someone else's bytes in it.
    /// </summary>
    [Fact]
    public async Task a_retried_batch_still_writes_every_binary_payload()
    {
        const string schema = "memorypack_pooled_retry";

        await using var store = await BuildStore(schema, EventAppendMode.QuickWithServerTimestamps, opts =>
        {
            opts.CommandTimeout = 1;
            opts.ConfigureWritePolly(builder => builder.AddRetry(new()
            {
                ShouldHandle = new PredicateBuilder().Handle<NpgsqlException>().Handle<MartenCommandException>(),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(50),
                OnRetry = _ =>
                {
                    churnTheSharedBytePool();
                    return default;
                }
            }));
        });

        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync(TestContext.Current.CancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"drop sequence if exists {schema}.attempt_counter; create sequence {schema}.attempt_counter;";
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var streams = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToArray();
        var appended = streams.ToDictionary(id => id, MixedEvents);

        await using (var session = store.LightweightSession())
        {
            foreach (var id in streams) session.Events.StartStream(id, appended[id]);

            // Sleeps past the command timeout on the first attempt only; nextval survives the rollback.
            session.QueueSqlCommand(
                $"select pg_sleep(case when nextval('{schema}.attempt_counter') <= 1 then 3 else 0 end)");

            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync(TestContext.Current.CancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"select last_value from {schema}.attempt_counter";
            // The batch really was executed more than once.
            Convert.ToInt64(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).ShouldBeGreaterThan(1);
        }

        foreach (var id in streams)
        {
            await assertRowsMatch(store, id, appended[id]);
            await assertReadsBack(store, id, appended[id]);
        }
    }

    [Fact]
    public void the_buffer_writer_member_writes_the_same_bytes_as_the_array_member()
    {
        var serializer = new MemoryPackEventSerializer();
        var data = new RouteRecorded(Guid.NewGuid(), LongRoute);

        var expected = serializer.Serialize(typeof(RouteRecorded), data);

        var writer = new ArrayBufferWriter<byte>();
        serializer.Serialize(typeof(RouteRecorded), data, writer);

        writer.WrittenSpan.ToArray().ShouldBe(expected);
    }

    [Fact]
    public void the_span_member_reads_a_slice_of_a_larger_buffer()
    {
        var serializer = new MemoryPackEventSerializer();
        var data = new PassengerPickedUp(Guid.NewGuid(), "Grace", DateTimeOffset.UtcNow);
        var bytes = serializer.Serialize(typeof(PassengerPickedUp), data);

        // A rented buffer is usually longer than the payload, and whatever follows the payload is not ours.
        var larger = new byte[bytes.Length + 64];
        larger.AsSpan().Fill(0xEE);
        bytes.CopyTo(larger, 0);

        serializer.Deserialize(typeof(PassengerPickedUp), new ReadOnlySpan<byte>(larger, 0, bytes.Length))
            .ShouldBe(data);
    }

    private static void churnTheSharedBytePool()
    {
        foreach (var size in new[] { 256, 512, 1024, 2048, 4096, 8192 })
        {
            var rentals = new List<byte[]>();
            for (var i = 0; i < 8; i++)
            {
                var rented = ArrayPool<byte>.Shared.Rent(size);
                rented.AsSpan().Fill(0xEE);
                rentals.Add(rented);
            }

            foreach (var rented in rentals) ArrayPool<byte>.Shared.Return(rented);
        }
    }

    // What is on disk: a binary event's bdata is exactly MemoryPack's bytes for it and its data the {}
    // placeholder; a JSON event has NULL bdata.
    private static async Task assertRowsMatch(DocumentStore store, Guid streamId, object[] appended)
    {
        await using var conn = store.Storage.Database.CreateConnection();
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"select bdata, data::text from {store.Options.Events.DatabaseSchemaName}.mt_events where stream_id = $1 order by version";
        var p = cmd.CreateParameter();
        p.Value = streamId;
        cmd.Parameters.Add(p);

        var rows = new List<(byte[]? bdata, string data)>();
        await using (var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                rows.Add((reader.IsDBNull(0) ? null : (byte[])reader.GetValue(0), reader.GetString(1)));
            }
        }

        rows.Count.ShouldBe(appended.Length);
        for (var i = 0; i < appended.Length; i++)
        {
            var data = appended[i];
            if (data is TripCommentAdded)
            {
                rows[i].bdata.ShouldBeNull($"row {i} is a JSON event");
                rows[i].data.ShouldNotBe("{}");
            }
            else
            {
                rows[i].bdata.ShouldBe(MemoryPackSerializer.Serialize(data.GetType(), data), $"row {i}");
                rows[i].data.ShouldBe("{}");
            }
        }
    }

    private static async Task assertReadsBack(DocumentStore store, Guid streamId, object[] appended)
    {
        await using var query = store.QuerySession();

        var events = await query.Events.FetchStreamAsync(streamId, token: TestContext.Current.CancellationToken);
        events.Select(x => comparable(x.Data)).ShouldBe(appended.Select(comparable));

        var raw = await query.Events.QueryAllRawEvents()
            .Where(x => x.StreamId == streamId)
            .OrderBy(x => x.Version)
            .ToListAsync(TestContext.Current.CancellationToken);
        raw.Select(x => comparable(x.Data)).ShouldBe(appended.Select(comparable));
    }

    // The JSON event is compared by its comment alone, so the assertion does not depend on how the harness's
    // JSON serializer rounds a DateTimeOffset. Binary events come back exactly as MemoryPack wrote them.
    private static object comparable(object data) => data is TripCommentAdded comment ? comment.Comment : data;
}
