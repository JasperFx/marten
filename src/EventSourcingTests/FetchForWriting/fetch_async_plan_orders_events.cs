using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Fetching;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Fetching;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

public class fetch_async_plan_orders_events: OneOffConfigurationsContext
{
    private readonly MartenTestAggregateWriteCache _cache = new();

    private void configure(bool useCache)
    {
        // One schema per test: the sequential scan below is forced by dropping an index on
        // mt_events, and the supervisor can run these two tests in separate processes.
        _schemaName = useCache ? "fetch_async_plan_orders_cached" : "fetch_async_plan_orders_full";

        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<WidgetAggregate>(SnapshotLifecycle.Async);
            if (useCache)
            {
                opts.Events.AggregateWriteCaching.Cache = _cache;
                opts.Events.CacheAggregatesForWriting<WidgetAggregate>();
            }
        });
    }

    [Fact]
    public async Task fetch_for_writing_applies_events_in_sequence_order_when_heap_order_differs()
    {
        configure(false);

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<WidgetAggregate>(streamId,
            new WidgetChanged(1), new WidgetChanged(2), new WidgetChanged(3));
        await theSession.SaveChangesAsync();

        await moveVersionToEndOfHeapAndForceSequentialScan(streamId, 1, 2, 3, 1);

        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<WidgetAggregate>(streamId);

        stream.Aggregate.ShouldNotBeNull();
        stream.Aggregate.Steps.ShouldBe(new[] { 1, 2, 3 });
    }

    [Fact]
    public async Task cache_hit_applies_delta_events_in_sequence_order_when_heap_order_differs()
    {
        configure(true);

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<WidgetAggregate>(streamId, new WidgetChanged(1));
        await theSession.SaveChangesAsync();

        await using (var warmup = theStore.LightweightSession())
        {
            await warmup.Events.FetchForWriting<WidgetAggregate>(streamId);
        }

        await using (var append = theStore.LightweightSession())
        {
            append.Events.Append(streamId, new WidgetChanged(2), new WidgetChanged(3));
            await append.SaveChangesAsync();
        }

        await moveVersionToEndOfHeapAndForceSequentialScan(streamId, 2, 1, 3, 2);

        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<WidgetAggregate>(streamId);

        stream.Aggregate.ShouldNotBeNull();
        stream.Aggregate.Steps.ShouldBe(new[] { 1, 2, 3 });
    }

    private async Task moveVersionToEndOfHeapAndForceSequentialScan(Guid streamId, long versionToMove,
        params long[] expectedPhysicalOrder)
    {
        await using var connection = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await connection.OpenAsync();

        // A no-op UPDATE writes a new tuple, which lands at the end of the heap.
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                $"update {SchemaName}.mt_events set timestamp = timestamp where stream_id = @stream and version = @version";
            command.Parameters.AddWithValue("stream", streamId);
            command.Parameters.AddWithValue("version", versionToMove);
            await command.ExecuteNonQueryAsync();
        }

        var physicalOrder = new List<long>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                $"select version from {SchemaName}.mt_events where stream_id = @stream order by ctid";
            command.Parameters.AddWithValue("stream", streamId);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                physicalOrder.Add(reader.GetInt64(0));
            }
        }

        physicalOrder.ShouldBe(expectedPhysicalOrder);

        // With (stream_id, version) gone, the only index left on mt_events is the seq_id primary
        // key, which a stream_id predicate cannot use -- so the fetch has to read the heap, in heap
        // order. Dropping the index rather than turning off enable_indexscan on the store's
        // connection keeps Weasel's information_schema introspection on its fast plans: forcing
        // heap scans store-wide makes the primary key lookup in Table.FetchExisting take tens of
        // seconds once a database holds a few thousand tables, which is what the CI database looks
        // like by the time this test runs.
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"drop index {SchemaName}.pk_mt_events_stream_and_version";
            await command.ExecuteNonQueryAsync();
        }

        await assertFetchQueryDoesASequentialScan(connection, streamId);
    }

    private async Task assertFetchQueryDoesASequentialScan(NpgsqlConnection connection, Guid streamId)
    {
        var plan = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            // FetchAsyncPlan's real query also joins the aggregate table, which does not exist until
            // the first fetch creates it. It does not need to be in here: with (stream_id, version)
            // dropped there is no index left that a stream_id predicate can use, so mt_events is
            // read sequentially whatever else the plan does.
            command.CommandText =
                $"explain (costs off) select d.seq_id from {SchemaName}.mt_events as d where d.stream_id = @stream and d.version > 0";
            command.Parameters.AddWithValue("stream", streamId);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                plan.Add(reader.GetString(0));
            }
        }

        string.Join("\n", plan).ShouldContain("Seq Scan on mt_events");
    }
}

public record WidgetChanged(int Step);

public class WidgetAggregate
{
    public Guid Id { get; set; }

    public List<int> Steps { get; set; } = new();

    public void Apply(WidgetChanged @event)
    {
        Steps.Add(@event.Step);
    }
}
