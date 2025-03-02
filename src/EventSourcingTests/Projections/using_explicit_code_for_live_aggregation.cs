using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using EventSourcingTests.FetchForWriting;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql.Tables;
using Xunit;

namespace EventSourcingTests.Projections;

public class using_explicit_code_for_live_aggregation : OneOffConfigurationsContext
{
    [Fact]
    public async Task using_a_custom_projection_for_live_aggregation()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add(new ExplicitCounter(), ProjectionLifecycle.Live);
        });

        var streamId = theSession.Events.StartStream<SimpleAggregate>(new AEvent(), new AEvent(), new BEvent(), new CEvent(), new CEvent(), new CEvent()).Id;
        await theSession.SaveChangesAsync();

        var aggregate = await theSession.Events.AggregateStreamAsync<SimpleAggregate>(streamId);
        aggregate.ACount.ShouldBe(2);
        aggregate.BCount.ShouldBe(1);
        aggregate.CCount.ShouldBe(3);
        aggregate.Id.ShouldBe(streamId);
    }

    [Fact]
    public async Task using_a_custom_projection_for_live_aggregation_that_has_string_as_id()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add(new ExplicitCounterThatHasStringId(), ProjectionLifecycle.Live);
            opts.Events.StreamIdentity = StreamIdentity.AsString;
        });

        var streamId = Guid.NewGuid().ToString();
        theSession.Events.StartStream<SimpleAggregateAsString>(streamId, new AEvent(), new AEvent(), new BEvent(), new CEvent(), new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        var aggregate = await theSession.Events.AggregateStreamAsync<SimpleAggregateAsString>(streamId);
        aggregate.ACount.ShouldBe(2);
        aggregate.BCount.ShouldBe(1);
        aggregate.CCount.ShouldBe(3);
        aggregate.Id.ShouldBe(streamId);
    }

    [Fact]
    public async Task using_a_custom_projection_for_live_aggregation_with_query_session()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add(new ExplicitCounter(), ProjectionLifecycle.Live);
        });

        var streamId = theSession.Events.StartStream<SimpleAggregate>(new AEvent(), new AEvent(), new BEvent(), new CEvent(), new CEvent(), new CEvent()).Id;
        await theSession.SaveChangesAsync();

        await using var query = theStore.QuerySession();

        var aggregate = await query.Events.AggregateStreamAsync<SimpleAggregate>(streamId);
        aggregate.ACount.ShouldBe(2);
        aggregate.BCount.ShouldBe(1);
        aggregate.CCount.ShouldBe(3);
        aggregate.Id.ShouldBe(streamId);
    }

    [Fact]
    public async Task does_not_create_tables()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add(new ExplicitCounter(), ProjectionLifecycle.Live);
        });

        var streamId = theSession.Events.StartStream<SimpleAggregate>(new AEvent(), new AEvent(), new BEvent(), new CEvent(), new CEvent(), new CEvent()).Id;
        await theSession.SaveChangesAsync();
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var eventStream = await theSession.Events.FetchForWriting<SimpleAggregate>(streamId);
        var tables = theStore.Storage.AllObjects().OfType<Table>();
        tables.ShouldNotContain(x => x.Identifier.Name.Contains(nameof(SimpleAggregate), StringComparison.OrdinalIgnoreCase));
    }
}

#region sample_using_simple_explicit_code_for_live_aggregation

public class ExplicitCounter: SingleStreamProjection<SimpleAggregate, Guid>
{
    public override SimpleAggregate Evolve(SimpleAggregate snapshot, Guid id, IEvent e)
    {
        snapshot ??= new SimpleAggregate();

        switch (e.Data)
        {
            case AEvent:
                snapshot.ACount++;
                break;
            case BEvent:
                snapshot.BCount++;
                break;
            case CEvent:
                snapshot.CCount++;
                break;
            case DEvent:
                snapshot.DCount++;
                break;
        }

        // You have to explicitly return the new value
        // of the aggregated document no matter what!
        return snapshot;
    }
}

#endregion

public class ExplicitCounterThatHasStringId: SingleStreamProjection<SimpleAggregateAsString, string>
{
    public override SimpleAggregateAsString Evolve(SimpleAggregateAsString snapshot, string id, IEvent e)
    {
        snapshot ??= new();
        switch (e.Data)
        {
            case AEvent:
                snapshot.ACount++;
                break;
            case BEvent:
                snapshot.BCount++;
                break;
            case CEvent:
                snapshot.CCount++;
                break;
            case DEvent:
                snapshot.DCount++;
                break;
        }

        return snapshot;
    }
}
