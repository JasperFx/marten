using System;
using System.Threading.Tasks;
using JasperFx.Events.Projections;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

public class batch_querying_by_natural_key: OneOffConfigurationsContext
{
    private void configure()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);
            opts.Projections.Snapshot<OrderSummary>(SnapshotLifecycle.Async);
            opts.Projections.Snapshot<InvoiceAggregate>(SnapshotLifecycle.Inline);
        });
    }

    private async Task<Guid> startOrder(OrderNumber number, int items)
    {
        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<OrderAggregate>(streamId, new OrderCreated(number, "Alice"));
        for (var i = 0; i < items; i++)
        {
            theSession.Events.Append(streamId, new OrderItemAdded("Widget", 10m));
        }

        await theSession.SaveChangesAsync();
        return streamId;
    }

    [Fact]
    public async Task fetch_latest_by_natural_keys_in_one_round_trip()
    {
        configure();

        var order = new OrderNumber("BATCH-1");
        var orderId = await startOrder(order, 2);

        var invoice = new InvoiceNumber("INV-BATCH-1");
        theSession.Events.StartStream<InvoiceAggregate>(Guid.NewGuid(), new InvoiceCreated(invoice, 99m));
        await theSession.SaveChangesAsync();

        await using var session = theStore.LightweightSession();
        var before = session.RequestCount;

        #region sample_batch_fetch_by_natural_key
        var batch = session.CreateBatchQuery();

        var summary = batch.Events.FetchLatest<OrderSummary, OrderNumber>(order);
        var owner = batch.Events.FetchLatest<OrderAggregate, OrderNumber>(order);
        var byStreamId = batch.Events.FetchLatest<OrderAggregate>(orderId);
        var invoiceAggregate = batch.Events.FetchLatest<InvoiceAggregate, InvoiceNumber>(invoice);
        var missing = batch.Events.FetchLatest<InvoiceAggregate, InvoiceNumber>(new InvoiceNumber("INV-NONE"));

        // One round trip for all five
        await batch.Execute();
        #endregion

        session.RequestCount.ShouldBe(before + 1);

        (await summary)!.Items.ShouldBe(2);
        (await owner)!.TotalAmount.ShouldBe(20m);
        (await byStreamId)!.Id.ShouldBe(orderId);
        (await invoiceAggregate)!.Amount.ShouldBe(99m);
        (await missing).ShouldBeNull();
    }

    [Fact]
    public async Task async_fetches_by_natural_key_go_anywhere_in_a_batch_beside_an_exclusive_fetch()
    {
        configure();

        var first = new OrderNumber("BATCH-MIXED-1");
        var second = new OrderNumber("BATCH-MIXED-2");
        var firstId = await startOrder(first, 1);
        var secondId = await startOrder(second, 2);

        await using var session = theStore.LightweightSession();
        var before = session.RequestCount;

        var batch = session.CreateBatchQuery();
        var owner = batch.Events.FetchLatest<OrderAggregate, OrderNumber>(second);
        var exclusive = batch.Events.FetchForExclusiveWriting<OrderAggregate, OrderNumber>(first);
        var writing = batch.Events.FetchForWriting<OrderSummary, OrderNumber>(first);
        var latest = batch.Events.FetchLatest<OrderSummary, OrderNumber>(second);
        await batch.Execute();

        session.RequestCount.ShouldBe(before + 1);

        (await owner)!.Id.ShouldBe(secondId);
        var locked = await exclusive;
        locked.Id.ShouldBe(firstId);
        var stream = await writing;
        stream.Id.ShouldBe(firstId);
        stream.Aggregate!.Items.ShouldBe(1);
        (await latest)!.Items.ShouldBe(2);

        // The exclusive fetch's row lock lasts until SaveChangesAsync
        await using var other = theStore.LightweightSession();
        var blocked = other.Events.FetchForExclusiveWriting<OrderAggregate, OrderNumber>(first);
        await aSessionIsWaitingOnALockAsync();
        blocked.IsCompleted.ShouldBeFalse();

        locked.AppendOne(new OrderItemAdded("Widget", 10m));
        await session.SaveChangesAsync();
        (await blocked).Aggregate!.TotalAmount.ShouldBe(20m);
    }

    /// <summary>Waits until some session in this database is blocked on a lock.</summary>
    private static async Task aSessionIsWaitingOnALockAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select exists (select 1 from pg_stat_activity " +
                          "where datname = current_database() and wait_event_type = 'Lock')";

        var giveUp = DateTime.UtcNow.AddSeconds(30);
        while (!(bool)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!)
        {
            if (DateTime.UtcNow > giveUp) throw new TimeoutException("No session ever waited on a lock");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}
