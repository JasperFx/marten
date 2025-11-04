using System;
using System.Threading.Tasks;
using DaemonTests.Aggregations;
using DaemonTests.MultiTenancy;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections;
using Marten.Patching;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DaemonTests.EventProjections;

public class using_patches_in_async_mode : OneOffConfigurationsContext
{
    [Fact]
    public async Task do_some_patching()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add(new LetterPatcher(), ProjectionLifecycle.Async);
        });

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();

        theSession.Events.StartStream<SimpleAggregate>(id1, new StartAggregate(), new MTAEvent(), new MTAEvent(), new MTBEvent());
        theSession.Events.StartStream<SimpleAggregate>(id2, new StartAggregate(), new MTAEvent(), new MTCEvent(), new MTCEvent());
        theSession.Events.StartStream<SimpleAggregate>(id3, new StartAggregate(), new MTBEvent(), new MTBEvent(), new MTBEvent(), new MTCEvent());

        for (int i = 0; i < 100; i++)
        {
            theSession.Events.StartStream<SimpleAggregate>(new StartAggregate(), new MTAEvent(), new MTAEvent(), new MTBEvent());
        }

        await theSession.SaveChangesAsync();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(20.Seconds());

        var aggregate1 = await theSession.LoadAsync<SimpleAggregate>(id1);
        aggregate1.ACount.ShouldBe(2);
        aggregate1.BCount.ShouldBe(1);

        var aggregate2 = await theSession.LoadAsync<SimpleAggregate>(id2);
        aggregate2.CCount.ShouldBe(2);


    }
}

public record StartAggregate;

public class LetterPatcher: EventProjection
{
    public SimpleAggregate Transform(IEvent<StartAggregate> e) => new SimpleAggregate { Id = e.StreamId };

    public void Project(IEvent<MTAEvent> e, IDocumentOperations ops)
    {
        ops.Patch<SimpleAggregate>(e.StreamId).Increment(x => x.ACount);
    }

    public void Project(IEvent<MTBEvent> e, IDocumentOperations ops)
    {
        ops.Patch<SimpleAggregate>(e.StreamId).Increment(x => x.BCount);
    }

    public void Project(IEvent<MTCEvent> e, IDocumentOperations ops)
    {
        ops.Patch<SimpleAggregate>(e.StreamId).Increment(x => x.CCount);
    }
}


