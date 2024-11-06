using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing;

public class hierarchy_projection
{
    private readonly ITestOutputHelper _output;

    public hierarchy_projection(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task try_to_use_hierarchical_with_live()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.Projections.Add(new ThingProjection(), ProjectionLifecycle.Live);
            opts.Schema.For<Thing>().AddSubClass<BigThing>().AddSubClass<SmallThing>();
            opts.DatabaseSchemaName = "things";
            opts.Logger(new TestOutputMartenLogger(_output));
        });

        using var session = store.LightweightSession();
        var id1 = session.Events.StartStream<Thing>(new ThingStarted("small"), new ThingFed()).Id;
        var id2 = session.Events.StartStream<Thing>(new ThingStarted("big"), new ThingFed()).Id;
        await session.SaveChangesAsync();

        var thing1 = await session.Events.AggregateStreamAsync<Thing>(id1);
        thing1.ShouldBeOfType<SmallThing>();
        thing1.IsFed.ShouldBeTrue();

        var thing2 = await session.Events.AggregateStreamAsync<Thing>(id2);
        thing2.ShouldBeOfType<BigThing>();
        thing2.IsFed.ShouldBeFalse(); // needs three meals
    }

    [Fact]
    public async Task try_to_use_hierarchical_with_inline()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.Projections.Add(new ThingProjection(), ProjectionLifecycle.Inline);
            opts.Schema.For<Thing>().AddSubClass<BigThing>().AddSubClass<SmallThing>();
            opts.DatabaseSchemaName = "things";
            opts.Logger(new TestOutputMartenLogger(_output));
        });

        await store.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Thing));

        using var session = store.LightweightSession();
        var id1 = session.Events.StartStream<Thing>(new ThingStarted("small"), new ThingFed()).Id;
        var id2 = session.Events.StartStream<Thing>(new ThingStarted("big"), new ThingFed()).Id;
        await session.SaveChangesAsync();

        var thing1 = await session.LoadAsync<Thing>(id1);
        thing1.ShouldBeOfType<SmallThing>();
        thing1.IsFed.ShouldBeTrue();

        var thing2 = await session.LoadAsync<Thing>(id2);
        thing2.ShouldBeOfType<BigThing>();
        thing2.IsFed.ShouldBeFalse(); // needs three meals
    }
}

public abstract class Thing
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string ShirtSize { get; set; }

    public abstract void Feed();

    public bool IsFed { get; set; }
}

public class SmallThing: Thing
{
    public override void Feed()
    {
        IsFed = true;
    }
}

public class BigThing: Thing
{
    public int Meals { get; set; }

    public override void Feed()
    {
        Meals++;
        if (Meals >= 3)
        {
            IsFed = true;
        }
    }
}

public record ThingStarted(string Size);

public record ThingFed;

public class ThingProjection: CustomProjection<Thing, Guid>
{
    public ThingProjection()
    {
        AggregateByStream();
    }

    public override Thing Apply(Thing snapshot, IReadOnlyList<IEvent> events)
    {
        if (snapshot == null)
        {
            var started = events.Select(x => x.Data).OfType<ThingStarted>().First();
            snapshot = started.Size.EqualsIgnoreCase("small") ? new SmallThing() : new BigThing();

            // Unexpected wrinkle here, you'll need to set the right identity here
            snapshot.Id = events[0].StreamId;
        }

        foreach (var thing in events.Select(x => x.Data).OfType<ThingFed>())
        {
            snapshot?.Feed();
        }

        return snapshot;
    }
}
