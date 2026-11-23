using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events.Daemon;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

#region sample_mapped_natural_key_aggregate

// An async read model of an order stream. It owns no lookup: the order number resolves through
// OrderAggregate's mt_natural_key table, which OrderAggregate's projection maintains inline.
public class OrderSummary
{
    public Guid Id { get; set; }

    [MappedNaturalKey<OrderAggregate>]
    public OrderNumber Number { get; set; }

    public int Items { get; set; }

    public static OrderSummary Create(OrderCreated e) => new() { Number = e.OrderNumber };

    public void Apply(OrderItemAdded e) => Items++;

    public void Apply(OrderNumberChanged e) => Number = e.NewOrderNumber;
}

#endregion

public class mapped_and_async_natural_keys: OneOffConfigurationsContext
{
    [Fact]
    public async Task an_async_aggregate_that_owns_its_key_keeps_its_lookup_through_the_daemon_and_a_rebuild()
    {
        StoreOptions(opts => opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Async));
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();

        var orderNumber = new OrderNumber("ASYNC-OWNED-REBUILD");
        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<OrderAggregate>(streamId,
            new OrderCreated(orderNumber, "Alice"),
            new OrderItemAdded("Widget", 9.99m));
        await theSession.SaveChangesAsync();

        // The lookup row is written inline with the events, before any daemon runs
        var lookupRows = $"select count(*) from {theStore.Options.Events.DatabaseSchemaName}.mt_natural_key_orderaggregate";
        (await countAsync(lookupRows)).ShouldBe(1);
        (await theSession.LoadAsync<OrderAggregate>(streamId)).ShouldBeNull();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(30.Seconds());
        await daemon.StopAllAsync();

        await using (var query = theStore.QuerySession())
        {
            (await query.LoadAsync<OrderAggregate>(streamId))!.OrderNum.ShouldBe(orderNumber);
        }

        await daemon.RebuildProjectionAsync<OrderAggregate>(CancellationToken.None);

        (await countAsync(lookupRows)).ShouldBe(1);

        await using var session = theStore.LightweightSession();
        var latest = await session.Events.FetchLatest<OrderAggregate, OrderNumber>(orderNumber);
        latest.ShouldNotBeNull();
        latest.Id.ShouldBe(streamId);
        latest.TotalAmount.ShouldBe(9.99m);
    }

    private async Task<long> countAsync(string sql)
    {
        await using var conn = theStore.Storage.Database.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task a_mapped_key_fetches_an_async_aggregate_through_the_owners_lookup()
    {
        StoreOptions(opts =>
        {
            #region sample_registering_mapped_natural_key_projections
            // The owner maintains the order number lookup inline...
            opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);

            // ...and the async view is fetched through it
            opts.Projections.Snapshot<OrderSummary>(SnapshotLifecycle.Async);
            #endregion
        });

        var orderNumber = new OrderNumber("MAPPED-1");
        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<OrderAggregate>(streamId,
            new OrderCreated(orderNumber, "Bob"),
            new OrderItemAdded("Widget", 9.99m),
            new OrderItemAdded("Gadget", 19.99m));
        await theSession.SaveChangesAsync();

        #region sample_fetch_async_aggregate_by_mapped_natural_key
        // Current state, whether or not the daemon has caught up yet
        var latest = await theSession.Events.FetchLatest<OrderSummary, OrderNumber>(orderNumber);
        #endregion

        latest.ShouldNotBeNull();
        latest.Id.ShouldBe(streamId);
        latest.Items.ShouldBe(2);

        var stream = await theSession.Events.FetchForWriting<OrderSummary, OrderNumber>(orderNumber);
        stream.Id.ShouldBe(streamId);
        stream.CurrentVersion.ShouldBe(3);
        stream.AppendOne(new OrderItemAdded("Doohickey", 5.50m));
        await theSession.SaveChangesAsync();

        (await theSession.Events.FetchLatest<OrderSummary, OrderNumber>(orderNumber))!.Items.ShouldBe(3);
    }

    [Fact]
    public async Task a_mapped_key_follows_the_owner_when_the_key_changes()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);
            opts.Projections.Snapshot<OrderSummary>(SnapshotLifecycle.Async);
        });

        var original = new OrderNumber("MAPPED-OLD");
        var renamed = new OrderNumber("MAPPED-NEW");
        theSession.Events.StartStream<OrderAggregate>(Guid.NewGuid(), new OrderCreated(original, "Carol"));
        await theSession.SaveChangesAsync();

        var stream = await theSession.Events.FetchForWriting<OrderSummary, OrderNumber>(original);
        stream.AppendOne(new OrderNumberChanged(renamed));
        await theSession.SaveChangesAsync();

        (await theSession.Events.FetchLatest<OrderSummary, OrderNumber>(original)).ShouldBeNull();
        (await theSession.Events.FetchLatest<OrderSummary, OrderNumber>(renamed))!.Number.ShouldBe(renamed);
    }

    [Fact]
    public void a_mapped_key_whose_source_is_not_registered_fails_when_the_store_is_built()
    {
        var ex = Should.Throw<InvalidProjectionException>(() =>
        {
            using var store = DocumentStore.For(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
                opts.Projections.Snapshot<OrderSummary>(SnapshotLifecycle.Async);
            });
        });

        ex.Message.ShouldContain("OrderSummary.Number");
        ex.Message.ShouldContain("no aggregate projection for that type is registered");
    }

    [Fact]
    public void a_mapped_key_and_its_source_must_agree_on_global_tenancy()
    {
        var ex = Should.Throw<InvalidProjectionException>(() =>
        {
            using var store = DocumentStore.For(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
                opts.Events.TenancyStyle = TenancyStyle.Conjoined;
                opts.Policies.AllDocumentsAreMultiTenanted();
                opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);
                opts.Projections.AddGlobalProjection(new SingleStreamProjection<OrderSummary, Guid>(),
                    ProjectionLifecycle.Async);
            });
        });

        ex.Message.ShouldContain("only one of the two is a global aggregate");
    }
}
