using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace EventSourcingTests.Projections;

public class hierarchy_projection : OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;

    public hierarchy_projection(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task try_to_use_hierarchical_with_live()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add(new ThingProjection(), ProjectionLifecycle.Live);
            opts.Schema.For<HThing>().AddSubClass<BigThing>().AddSubClass<SmallThing>();
        });

        var id1 = theSession.Events.StartStream<Thing>(new ThingStarted("small"), new ThingFed()).Id;
        var id2 = theSession.Events.StartStream<Thing>(new ThingStarted("big"), new ThingFed()).Id;
        await theSession.SaveChangesAsync();

        var thing1 = await theSession.Events.AggregateStreamAsync<HThing>(id1);
        thing1.ShouldBeOfType<SmallThing>();
        thing1.IsFed.ShouldBeTrue();

        var thing2 = await theSession.Events.AggregateStreamAsync<HThing>(id2);
        thing2.ShouldBeOfType<BigThing>();
        thing2.IsFed.ShouldBeFalse(); // needs three meals
    }

    [Fact]
    public async Task try_to_use_hierarchical_with_inline()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add(new ThingProjection(), ProjectionLifecycle.Inline);
            opts.Schema.For<HThing>().AddSubClass<BigThing>().AddSubClass<SmallThing>();
        });

        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(HThing));

        var id1 = theSession.Events.StartStream<HThing>(new ThingStarted("small"), new ThingFed()).Id;
        var id2 = theSession.Events.StartStream<HThing>(new ThingStarted("big"), new ThingFed()).Id;
        await theSession.SaveChangesAsync();

        var thing1 = await theSession.LoadAsync<HThing>(id1);
        thing1.ShouldBeOfType<SmallThing>();
        thing1.IsFed.ShouldBeTrue();

        var thing2 = await theSession.LoadAsync<HThing>(id2);
        thing2.ShouldBeOfType<BigThing>();
        thing2.IsFed.ShouldBeFalse(); // needs three meals
    }
}

public abstract class HThing
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string ShirtSize { get; set; }

    public abstract void Feed();

    public bool IsFed { get; set; }
}

public class SmallThing: HThing
{
    public override void Feed()
    {
        IsFed = true;
    }
}

public class BigThing: HThing
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

public class ThingProjection: SingleStreamProjection<HThing, Guid>
{
    public override ValueTask<(HThing?, ActionType)> DetermineActionAsync(IQuerySession session, HThing snapshot, Guid identity,
        IIdentitySetter<HThing, Guid> identitySetter, IReadOnlyList<IEvent> events,
        CancellationToken cancellation)
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

        return new ValueTask<(HThing, ActionType)>((snapshot, ActionType.Store));
    }
}
