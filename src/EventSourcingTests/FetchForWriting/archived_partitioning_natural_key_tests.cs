using System;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

public class archived_partitioning_natural_key_tests : OneOffConfigurationsContext
{
    [Fact]
    public async Task fetch_for_writing_with_archived_partitioning()
    {
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = true;
            opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);
        });

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<OrderAggregate>(streamId,
            new OrderCreated(new OrderNumber("ORD-001"), "Alice"));
        await theSession.SaveChangesAsync();

        var result = await theSession.Events.FetchForWriting<OrderAggregate, OrderNumber>(new OrderNumber("ORD-001"));
        result.ShouldNotBeNull();
        result.Aggregate.ShouldNotBeNull();
        result.Aggregate.CustomerName.ShouldBe("Alice");
    }

    [Fact]
    public async Task fetch_latest_with_archived_partitioning()
    {
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = true;
            opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);
        });

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<OrderAggregate>(streamId,
            new OrderCreated(new OrderNumber("ORD-002"), "Bob"));
        await theSession.SaveChangesAsync();

        var latest = await theSession.Events.FetchLatest<OrderAggregate, OrderNumber>(new OrderNumber("ORD-002"));
        latest.ShouldNotBeNull();
        latest.CustomerName.ShouldBe("Bob");
    }

    [Fact]
    public async Task natural_key_update_with_archived_partitioning()
    {
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = true;
            opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);
        });

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<OrderAggregate>(streamId,
            new OrderCreated(new OrderNumber("ORD-003"), "Carol"));
        await theSession.SaveChangesAsync();

        theSession.Events.Append(streamId,
            new OrderNumberChanged(new OrderNumber("ORD-003-UPDATED")));
        await theSession.SaveChangesAsync();

        var latest = await theSession.Events.FetchLatest<OrderAggregate, OrderNumber>(
            new OrderNumber("ORD-003-UPDATED"));
        latest.ShouldNotBeNull();
        latest.CustomerName.ShouldBe("Carol");
    }

    [Fact]
    public async Task schema_creation_idempotent_with_archived_partitioning()
    {
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = true;
            opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);
        });

        await theStore.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent), default);
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }
}
