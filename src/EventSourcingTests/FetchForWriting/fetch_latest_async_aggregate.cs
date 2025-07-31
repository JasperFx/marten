using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Events;
using Marten.Events.Projections;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

public class fetch_latest_async_aggregate: OneOffConfigurationsContext
{
    [Fact]
    public async Task from_no_current_activity_guid_centric()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async);
        });

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent(), new BEvent(), new CEvent(),
            new CEvent(), new CEvent());

        await theSession.SaveChangesAsync();

        await using var query = theStore.LightweightSession();
        var document = await query.Events.FetchLatest<SimpleAggregate>(streamId);

        document.ACount.ShouldBe(1);
        document.BCount.ShouldBe(2);
        document.CCount.ShouldBe(3);
    }

    [Fact]
    public async Task from_no_current_activity_guid_centric_as_batch()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async);
        });

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent(), new BEvent(), new CEvent(),
            new CEvent(), new CEvent());

        await theSession.SaveChangesAsync();

        await using var query = theStore.LightweightSession();
        var batch = query.CreateBatchQuery();
        var documentQuery = batch.Events.FetchLatest<SimpleAggregate>(streamId);
        await batch.Execute();

        var document = await documentQuery;

        document.ACount.ShouldBe(1);
        document.BCount.ShouldBe(2);
        document.CCount.ShouldBe(3);
    }


    [Fact]
    public async Task from_no_current_activity_string_centric()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<SimpleAggregateAsString>(SnapshotLifecycle.Async);
            opts.Events.StreamIdentity = StreamIdentity.AsString;
        });

        var streamId = Guid.NewGuid().ToString();
        theSession.Events.StartStream<SimpleAggregateAsString>(streamId, new AEvent(), new BEvent(), new BEvent(), new CEvent(),
            new CEvent(), new CEvent());

        await theSession.SaveChangesAsync();

        await using var query = theStore.LightweightSession();
        var document = await query.Events.FetchLatest<SimpleAggregateAsString>(streamId);

        document.ACount.ShouldBe(1);
        document.BCount.ShouldBe(2);
        document.CCount.ShouldBe(3);
    }

    [Fact]
    public async Task from_no_current_activity_string_centric_from_batch()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<SimpleAggregateAsString>(SnapshotLifecycle.Async);
            opts.Events.StreamIdentity = StreamIdentity.AsString;
        });

        var streamId = Guid.NewGuid().ToString();
        theSession.Events.StartStream<SimpleAggregateAsString>(streamId, new AEvent(), new BEvent(), new BEvent(), new CEvent(),
            new CEvent(), new CEvent());

        await theSession.SaveChangesAsync();

        await using var query = theStore.LightweightSession();


        var document = await query.Events.FetchLatest<SimpleAggregateAsString>(streamId);

        document.ACount.ShouldBe(1);
        document.BCount.ShouldBe(2);
        document.CCount.ShouldBe(3);
    }

    [Fact]
    public async Task from_after_fetch_for_writing_guid_centric_brand_new_1()
    {
        StoreOptions(opts =>
        {
            opts.Events.UseIdentityMapForAggregates = true;
            opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async);
        });

        var streamId = Guid.NewGuid();

        var stream = await theSession.Events.FetchForWriting<SimpleAggregate>(streamId);
        stream.AppendMany(new AEvent(), new BEvent(), new BEvent(), new CEvent(),
            new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        var aggregate = await theSession.Events.FetchLatest<SimpleAggregate>(streamId);
        aggregate.ACount.ShouldBe(1);
        aggregate.BCount.ShouldBe(2);
        aggregate.CCount.ShouldBe(3);
    }

    [Fact]
    public async Task from_after_fetch_for_writing_guid_centric_brand_new_1_from_batch()
    {
        StoreOptions(opts =>
        {
            opts.Events.UseIdentityMapForAggregates = true;
            opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async);
        });

        var streamId = Guid.NewGuid();

        var stream = await theSession.Events.FetchForWriting<SimpleAggregate>(streamId);
        stream.AppendMany(new AEvent(), new BEvent(), new BEvent(), new CEvent(),
            new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        var batch = theSession.CreateBatchQuery();

        var aggregateQuery = batch.Events.FetchLatest<SimpleAggregate>(streamId);
        await batch.Execute();

        var aggregate = await aggregateQuery;

        aggregate.ACount.ShouldBe(1);
        aggregate.BCount.ShouldBe(2);
        aggregate.CCount.ShouldBe(3);
    }

    [Fact]
    public async Task from_after_fetch_for_writing_guid_centric_brand_new_no_optimization()
    {
        StoreOptions(opts =>
        {
            //opts.Events.UseIdentityMapForAggregates = true;
            opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async);
        });

        var streamId = Guid.NewGuid();

        var stream = await theSession.Events.FetchForWriting<SimpleAggregate>(streamId);
        stream.AppendMany(new AEvent(), new BEvent(), new BEvent(), new CEvent(),
            new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        var aggregate = await theSession.Events.FetchLatest<SimpleAggregate>(streamId);
        aggregate.ACount.ShouldBe(1);
        aggregate.BCount.ShouldBe(2);
        aggregate.CCount.ShouldBe(3);
    }

    [Fact]
    public async Task from_after_fetch_for_writing_guid_centric_brand_new_no_optimization_from_batch()
    {
        StoreOptions(opts =>
        {
            //opts.Events.UseIdentityMapForAggregates = true;
            opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async);
        });

        var streamId = Guid.NewGuid();

        var stream = await theSession.Events.FetchForWriting<SimpleAggregate>(streamId);
        stream.AppendMany(new AEvent(), new BEvent(), new BEvent(), new CEvent(),
            new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        var batch = theSession.CreateBatchQuery();

        var aggregateQuery = batch.Events.FetchLatest<SimpleAggregate>(streamId);
        await batch.Execute();
        var aggregate = await aggregateQuery;

        aggregate.ACount.ShouldBe(1);
        aggregate.BCount.ShouldBe(2);
        aggregate.CCount.ShouldBe(3);
    }


    [Fact]
    public async Task from_after_fetch_for_writing_guid_centric_brand_existing()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async);
        });

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent(), new BEvent(), new CEvent(),
            new CEvent(), new CEvent());

        await theSession.SaveChangesAsync();

        using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<SimpleAggregate>(streamId);
        stream.AppendMany(new DEvent(), new DEvent());
        await session.SaveChangesAsync();

        await using var query = theStore.LightweightSession();
        var document = await query.Events.FetchLatest<SimpleAggregate>(streamId);

        document.ACount.ShouldBe(1);
        document.BCount.ShouldBe(2);
        document.CCount.ShouldBe(3);
        document.DCount.ShouldBe(2);
    }

    [Fact]
    public async Task from_after_fetch_for_writing_guid_centric_brand_existing_from_batch()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async);
        });

        var target1 = Target.Random();
        var target2 = Target.Random();
        theSession.Store(target1, target2);
        await theSession.SaveChangesAsync();

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent(), new BEvent(), new CEvent(),
            new CEvent(), new CEvent());

        await theSession.SaveChangesAsync();

        using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<SimpleAggregate>(streamId);
        stream.AppendMany(new DEvent(), new DEvent());
        await session.SaveChangesAsync();

        await using var query = theStore.LightweightSession();
        var batch = query.CreateBatchQuery();
        var targetQuery1 = batch.Load<Target>(target1.Id);
        var documentQuery = batch.Events.FetchLatest<SimpleAggregate>(streamId);
        var targetQuery2 = batch.Load<Target>(target2.Id);
        await batch.Execute();

        (await targetQuery1).ShouldNotBeNull();
        var document = await documentQuery;
        (await targetQuery2).ShouldNotBeNull();

        document.ACount.ShouldBe(1);
        document.BCount.ShouldBe(2);
        document.CCount.ShouldBe(3);
        document.DCount.ShouldBe(2);
    }

    [Fact]
    public async Task from_after_fetch_for_writing_string_centric_brand_new()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Projections.Snapshot<SimpleAggregateAsString>(SnapshotLifecycle.Async);
        });

        var streamId = Guid.NewGuid().ToString();

        var stream = await theSession.Events.FetchForWriting<SimpleAggregateAsString>(streamId);
        stream.AppendMany(new AEvent(), new BEvent(), new BEvent(), new CEvent(),
            new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        var aggregate = await theSession.Events.FetchLatest<SimpleAggregateAsString>(streamId);
        aggregate.ACount.ShouldBe(1);
        aggregate.BCount.ShouldBe(2);
        aggregate.CCount.ShouldBe(3);
    }

    [Fact]
    public async Task from_after_fetch_for_writing_string_centric_brand_new_with_batch()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Projections.Snapshot<SimpleAggregateAsString>(SnapshotLifecycle.Async);
        });

        var streamId = Guid.NewGuid().ToString();

        var stream = await theSession.Events.FetchForWriting<SimpleAggregateAsString>(streamId);
        stream.AppendMany(new AEvent(), new BEvent(), new BEvent(), new CEvent(),
            new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        var batch = theSession.CreateBatchQuery();

        var aggregateQuery = batch.Events.FetchLatest<SimpleAggregateAsString>(streamId);
        await batch.Execute();

        var aggregate = await aggregateQuery;

        aggregate.ACount.ShouldBe(1);
        aggregate.BCount.ShouldBe(2);
        aggregate.CCount.ShouldBe(3);
    }

    [Fact]
    public async Task from_after_fetch_for_writing_string_centric_existing()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Projections.Snapshot<SimpleAggregateAsString>(SnapshotLifecycle.Async);
        });

        var streamId = Guid.NewGuid().ToString();
        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent(), new BEvent(), new CEvent(),
            new CEvent(), new CEvent());

        await theSession.SaveChangesAsync();

        using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<SimpleAggregateAsString>(streamId);
        stream.AppendMany(new DEvent(), new DEvent());
        await session.SaveChangesAsync();

        await using var query = theStore.LightweightSession();
        var document = await query.Events.FetchLatest<SimpleAggregateAsString>(streamId);

        document.ACount.ShouldBe(1);
        document.BCount.ShouldBe(2);
        document.CCount.ShouldBe(3);
        document.DCount.ShouldBe(2);
    }

    [Fact]
    public async Task from_after_fetch_for_writing_string_centric_existing_with_batch()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Projections.Snapshot<SimpleAggregateAsString>(SnapshotLifecycle.Async);
        });

        var streamId = Guid.NewGuid().ToString();
        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent(), new BEvent(), new CEvent(),
            new CEvent(), new CEvent());

        await theSession.SaveChangesAsync();

        using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<SimpleAggregateAsString>(streamId);
        stream.AppendMany(new DEvent(), new DEvent());
        await session.SaveChangesAsync();

        await using var query = theStore.LightweightSession();
        var batch = session.CreateBatchQuery();
        var documentQuery = batch.Events.FetchLatest<SimpleAggregateAsString>(streamId);
        await batch.Execute();
        var document = await documentQuery;

        document.ACount.ShouldBe(1);
        document.BCount.ShouldBe(2);
        document.CCount.ShouldBe(3);
        document.DCount.ShouldBe(2);
    }

    [Fact]
    public async Task fetch_latest_immutable_aggregate_running_inline()
    {
        StoreOptions(opts =>
        {
            opts.Events.UseIdentityMapForAggregates = true;

            opts.Projections.Snapshot<TestAggregateImmutable>(SnapshotLifecycle.Inline);
        });

        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<TestAggregateImmutable>(streamId, new CreatedEvent(streamId, "Foo", new()), new UpdatedEvent(Guid.NewGuid(), "Bar", new()));
        await theSession.SaveChangesAsync();

        var aggregate = await theSession.Events.FetchLatest<TestAggregateImmutable>(streamId);
        aggregate.Name.ShouldBe("Bar");
        aggregate.Id.ShouldBe(streamId);

        using var session2 = theStore.LightweightSession();
        session2.Events.Append(streamId, new UpdatedEvent(Guid.NewGuid(), "Random", new()));
        await session2.SaveChangesAsync();

        var aggregate2 = await session2.Events.FetchLatest<TestAggregateImmutable>(streamId);
        aggregate2.Name.ShouldBe("Random");
    }

    [Fact]
    public async Task fetch_latest_immutable_aggregate_running_inline_and_identity_map()
    {
        StoreOptions(opts =>
        {
            opts.Events.UseIdentityMapForAggregates = true;

            opts.Projections.Snapshot<TestAggregateImmutable>(SnapshotLifecycle.Inline);
        });

        var streamId = Guid.NewGuid();

        using var session1 = theStore.IdentitySession();

        session1.Events.StartStream<TestAggregateImmutable>(streamId, new CreatedEvent(streamId, "Foo", new()), new UpdatedEvent(Guid.NewGuid(), "Bar", new()));
        await session1.SaveChangesAsync();

        var aggregate = await session1.Events.FetchLatest<TestAggregateImmutable>(streamId);
        aggregate.Name.ShouldBe("Bar");
        aggregate.Id.ShouldBe(streamId);

        using var session2 = theStore.IdentitySession();
        session2.Events.Append(streamId, new UpdatedEvent(Guid.NewGuid(), "Random", new()));
        await session2.SaveChangesAsync();

        var aggregate2 = await session2.Events.FetchLatest<TestAggregateImmutable>(streamId);
        aggregate2.Name.ShouldBe("Random");
    }

}

public record CreatedEvent(Guid Id, string Name, Dictionary<int, string> TestData);

public record UpdatedEvent(Guid Id, string Name, Dictionary<int, string> TestData);

public record TestAggregateImmutable(Guid Id, string Name, Dictionary<int, string> TestData)
{
    public static TestAggregateImmutable Create(IEvent<CreatedEvent> @event)
    {
        return new TestAggregateImmutable(@event.StreamId, @event.Data.Name, @event.Data.TestData);
    }

    public TestAggregateImmutable Apply(UpdatedEvent @event)
    {
        return this with { Name = @event.Name, TestData = @event.TestData };
    }
}
