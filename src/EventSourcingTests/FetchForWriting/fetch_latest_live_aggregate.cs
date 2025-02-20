using System;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Events;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

public class fetch_latest_live_aggregate: OneOffConfigurationsContext
{
    [Fact]
    public async Task from_no_current_activity_guid_centric()
    {
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
    public async Task from_no_current_activity_string_centric()
    {
        StoreOptions(opts =>
        {
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
    public async Task from_after_fetch_for_writing_guid_centric_brand_new_no_optimization()
    {
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
    public async Task from_after_fetch_for_writing_guid_centric_brand_new_with_optimization()
    {
        StoreOptions(opts => opts.Events.UseIdentityMapForAggregates = true);

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
    public async Task from_after_fetch_for_writing_guid_centric_brand_existing_no_optimization()
    {
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
    public async Task from_after_fetch_for_writing_guid_centric_brand_existing_with_optimization()
    {
        StoreOptions(opts => opts.Events.UseIdentityMapForAggregates = true);

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent(), new BEvent(), new CEvent(),
            new CEvent(), new CEvent());

        await theSession.SaveChangesAsync();

        using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<SimpleAggregate>(streamId);
        stream.AppendMany(new DEvent(), new DEvent());
        await session.SaveChangesAsync();

        var document = await session.Events.FetchLatest<SimpleAggregate>(streamId);

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
    public async Task from_after_fetch_for_writing_string_centric_existing()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
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

}
