using System;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Events.Projections;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

[Collection("OneOffs")]
public class conjoined_tenancy_natural_key_tests: OneOffConfigurationsContext
{
    private const string TenantA = "tenant-a";
    private const string TenantB = "tenant-b";

    [Fact]
    public async Task can_create_schema_with_conjoined_tenancy_and_natural_keys()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }

    [Fact]
    public async Task schema_is_idempotent_with_conjoined_tenancy_and_natural_keys()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var store2 = SeparateStore(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);
        });

        await store2.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }

    [Fact]
    public async Task can_create_schema_with_conjoined_tenancy_archived_partitioning_and_natural_keys()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseArchivedStreamPartitioning = true;
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }

    [Fact]
    public async Task same_natural_key_in_different_tenants()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);
        });

        var orderNumber = new OrderNumber("ORD-001");

        // Tenant A creates a stream with this natural key
        await using var sessionA = theStore.LightweightSession(TenantA);
        var streamA = Guid.NewGuid();
        sessionA.Events.StartStream<OrderAggregate>(streamA,
            new OrderCreated(orderNumber, "Alice"));
        await sessionA.SaveChangesAsync();

        // Tenant B creates a different stream with the SAME natural key
        await using var sessionB = theStore.LightweightSession(TenantB);
        var streamB = Guid.NewGuid();
        sessionB.Events.StartStream<OrderAggregate>(streamB,
            new OrderCreated(orderNumber, "Bob"));
        await sessionB.SaveChangesAsync();

        // Fetch by natural key from Tenant A
        await using var queryA = theStore.LightweightSession(TenantA);
        var aggA = await queryA.Events.FetchForWriting<OrderAggregate, OrderNumber>(orderNumber);
        aggA.Aggregate.ShouldNotBeNull();
        aggA.Aggregate.CustomerName.ShouldBe("Alice");

        // Fetch by natural key from Tenant B
        await using var queryB = theStore.LightweightSession(TenantB);
        var aggB = await queryB.Events.FetchForWriting<OrderAggregate, OrderNumber>(orderNumber);
        aggB.Aggregate.ShouldNotBeNull();
        aggB.Aggregate.CustomerName.ShouldBe("Bob");
    }

    [Fact]
    public async Task fetch_latest_by_natural_key_is_tenant_isolated()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);
        });

        var orderNumber = new OrderNumber("ORD-002");

        // Tenant A creates order
        await using var sessionA = theStore.LightweightSession(TenantA);
        sessionA.Events.StartStream<OrderAggregate>(Guid.NewGuid(),
            new OrderCreated(orderNumber, "Alice"),
            new OrderItemAdded("Widget", 10.00m));
        await sessionA.SaveChangesAsync();

        // Tenant B creates order with same number
        await using var sessionB = theStore.LightweightSession(TenantB);
        sessionB.Events.StartStream<OrderAggregate>(Guid.NewGuid(),
            new OrderCreated(orderNumber, "Bob"),
            new OrderItemAdded("Gadget", 20.00m));
        await sessionB.SaveChangesAsync();

        // FetchLatest from Tenant A
        await using var queryA = theStore.LightweightSession(TenantA);
        var aggA = await queryA.Events.FetchLatest<OrderAggregate, OrderNumber>(orderNumber);
        aggA.ShouldNotBeNull();
        aggA.CustomerName.ShouldBe("Alice");
        aggA.TotalAmount.ShouldBe(10.00m);

        // FetchLatest from Tenant B
        await using var queryB = theStore.LightweightSession(TenantB);
        var aggB = await queryB.Events.FetchLatest<OrderAggregate, OrderNumber>(orderNumber);
        aggB.ShouldNotBeNull();
        aggB.CustomerName.ShouldBe("Bob");
        aggB.TotalAmount.ShouldBe(20.00m);
    }

    [Fact]
    public async Task natural_key_returns_null_for_nonexistent_key_in_tenant()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);
        });

        var orderNumber = new OrderNumber("ORD-003");

        // Tenant A has the order
        await using var sessionA = theStore.LightweightSession(TenantA);
        sessionA.Events.StartStream<OrderAggregate>(Guid.NewGuid(),
            new OrderCreated(orderNumber, "Alice"));
        await sessionA.SaveChangesAsync();

        // Tenant B should NOT find it
        await using var queryB = theStore.LightweightSession(TenantB);
        var aggB = await queryB.Events.FetchLatest<OrderAggregate, OrderNumber>(orderNumber);
        aggB.ShouldBeNull();
    }

    [Fact]
    public async Task fetch_for_writing_appends_to_correct_tenant_stream()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);
        });

        var orderNumber = new OrderNumber("ORD-004");

        // Create streams in both tenants
        await using var sessionA = theStore.LightweightSession(TenantA);
        sessionA.Events.StartStream<OrderAggregate>(Guid.NewGuid(),
            new OrderCreated(orderNumber, "Alice"));
        await sessionA.SaveChangesAsync();

        await using var sessionB = theStore.LightweightSession(TenantB);
        sessionB.Events.StartStream<OrderAggregate>(Guid.NewGuid(),
            new OrderCreated(orderNumber, "Bob"));
        await sessionB.SaveChangesAsync();

        // Fetch for writing in Tenant A and append
        await using var writeA = theStore.LightweightSession(TenantA);
        var streamA = await writeA.Events.FetchForWriting<OrderAggregate, OrderNumber>(orderNumber);
        streamA.AppendOne(new OrderItemAdded("Widget", 10.00m));
        await writeA.SaveChangesAsync();

        // Verify Tenant A has the new event, Tenant B doesn't
        await using var verifyA = theStore.LightweightSession(TenantA);
        var aggA = await verifyA.Events.FetchLatest<OrderAggregate, OrderNumber>(orderNumber);
        aggA.ShouldNotBeNull();
        aggA.TotalAmount.ShouldBe(10.00m);

        await using var verifyB = theStore.LightweightSession(TenantB);
        var aggB = await verifyB.Events.FetchLatest<OrderAggregate, OrderNumber>(orderNumber);
        aggB.ShouldNotBeNull();
        aggB.TotalAmount.ShouldBe(0m);
    }

    [Fact]
    public async Task live_aggregation_with_conjoined_tenancy_and_natural_key()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Projections.LiveStreamAggregation<OrderAggregate>();
        });

        var orderNumber = new OrderNumber("ORD-LIVE-001");

        // Create stream in Tenant A
        await using var sessionA = theStore.LightweightSession(TenantA);
        sessionA.Events.StartStream<OrderAggregate>(Guid.NewGuid(),
            new OrderCreated(orderNumber, "Alice"),
            new OrderItemAdded("Widget", 15.00m));
        await sessionA.SaveChangesAsync();

        // Create stream in Tenant B with same key
        await using var sessionB = theStore.LightweightSession(TenantB);
        sessionB.Events.StartStream<OrderAggregate>(Guid.NewGuid(),
            new OrderCreated(orderNumber, "Bob"),
            new OrderItemAdded("Gadget", 25.00m));
        await sessionB.SaveChangesAsync();

        // Live aggregation via FetchForWriting from Tenant A
        await using var queryA = theStore.LightweightSession(TenantA);
        var streamA = await queryA.Events.FetchForWriting<OrderAggregate, OrderNumber>(orderNumber);
        streamA.Aggregate.ShouldNotBeNull();
        streamA.Aggregate.CustomerName.ShouldBe("Alice");
        streamA.Aggregate.TotalAmount.ShouldBe(15.00m);
    }
}
