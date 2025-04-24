using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using EventSourcingTests.Examples;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Grouping;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Internal.Sessions;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_3661_await_custom_projection_slicing : OneOffConfigurationsContext
{
    [Fact]
    public async Task fetching_multiple_items_from_slicers_in_async_custom_projection()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add(new StartAndStopIteratingAwaitablesSlicedProjection(), ProjectionLifecycle.Async);
        });

        var stream = Guid.NewGuid();
        theSession.Store(new Document1 { Id = stream });
        theSession.Events.StartStream(stream, new Start(), new Increment(), new Increment());

        var stream2 = Guid.NewGuid();
        theSession.Store(new Document1 { Id = stream2 });
        theSession.Events.StartStream(stream2, new Start(), new Increment(), new Increment());
        await theSession.SaveChangesAsync();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(20.Seconds());

        var aggregate = await theSession.LoadAsync<StartAndStopAggregate2>(stream);
        aggregate.Count.ShouldBe(2);
        var aggregate2 = await theSession.LoadAsync<StartAndStopAggregate2>(stream2);
        aggregate2.Count.ShouldBe(2);
    }
}

public class StartAndStopAggregate2
{
    public int Count { get; set; }

    public Guid Id { get; set; }

    public void Increment()
    {
        Count++;
    }
}

public class StartAndStopIteratingAwaitablesSlicedProjection: MultiStreamProjection<StartAndStopAggregate2, Guid>
{
    public StartAndStopIteratingAwaitablesSlicedProjection()
    {
        CustomGrouping(async (session, events, group) =>
        {
            foreach (var @event in events)
            {
                await session.LoadAsync<Document1>(@event.StreamId);
                group.AddEvent(@event.StreamId, @event);
            }
        });

        IncludeType<Start>();
        IncludeType<Increment>();
    }

    public StartAndStopAggregate2 Create(Start _) => new StartAndStopAggregate2();

    public void Apply(StartAndStopAggregate2 aggregate, Increment _) => aggregate.Increment();
}
