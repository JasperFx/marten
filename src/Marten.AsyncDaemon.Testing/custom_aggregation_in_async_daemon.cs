using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.AsyncDaemon.Testing.TestingSupport;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Internal.Sessions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.AsyncDaemon.Testing;

public class custom_aggregation_in_async_daemon : OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;

    public custom_aggregation_in_async_daemon(ITestOutputHelper output)
    {
        _output = output;
    }

    private void appendCustomEvent(int number, char letter)
    {
        TheSession.Events.Append(Guid.NewGuid(), new CustomEvent(number, letter));
    }

    [Fact]
    public async Task run_end_to_end()
    {
        StoreOptions(opts =>
        {
            var myCustomAggregation = new MyCustomProjection();
            opts.Projections.Add(myCustomAggregation, ProjectionLifecycle.Async);
            opts.Logger(new TestOutputMartenLogger(_output));
        });

        await TheStore.Advanced.Clean.DeleteAllDocumentsAsync();
        await TheStore.Advanced.Clean.DeleteAllEventDataAsync();

        appendCustomEvent(1, 'a');
        appendCustomEvent(1, 'a');
        appendCustomEvent(1, 'b');
        appendCustomEvent(1, 'c');
        appendCustomEvent(1, 'd');
        appendCustomEvent(2, 'a');
        appendCustomEvent(2, 'a');
        appendCustomEvent(3, 'b');
        appendCustomEvent(3, 'd');
        appendCustomEvent(1, 'a');
        appendCustomEvent(1, 'a');

        await TheSession.SaveChangesAsync();

        using var daemon = await TheStore.BuildProjectionDaemonAsync(logger:new TestLogger<IProjection>(_output));
        await daemon.StartAllShards();

        await  daemon.Tracker.WaitForShardState("Custom:All", 11);

        var agg1 = await TheSession.LoadAsync<CustomAggregate>(1);
        agg1
            .ShouldBe(new CustomAggregate{Id = 1, ACount = 4, BCount = 1, CCount = 1, DCount = 1});

        (await TheSession.LoadAsync<CustomAggregate>(2))
            .ShouldBe(new CustomAggregate{Id = 2, ACount = 2, BCount = 0, CCount = 0, DCount = 0});

        (await TheSession.LoadAsync<CustomAggregate>(3))
            .ShouldBe(new CustomAggregate{Id = 3, ACount = 0, BCount = 1, CCount = 0, DCount = 1});

    }
}


public class CustomEvent : INumbered
{
    public CustomEvent(int number, char letter)
    {
        Number = number;
        Letter = letter;
    }

    public int Number { get; set; }
    public char Letter { get; set; }
}

public interface INumbered
{
    public int Number { get; }
}


public class MyCustomProjection: CustomProjection<CustomAggregate, int>
{
    public MyCustomProjection()
    {
        ProjectionName = "Custom";

        Slicer = new EventSlicer<CustomAggregate, int>().Identity<INumbered>(x =>
            x.Number);
    }

    public override ValueTask ApplyChangesAsync(DocumentSessionBase session, EventSlice<CustomAggregate, int> slice, CancellationToken cancellation,
        ProjectionLifecycle lifecycle = ProjectionLifecycle.Inline)
    {
        var aggregate = slice.Aggregate ?? new CustomAggregate { Id = slice.Id };

        foreach (var @event in slice.Events())
        {
            if (@event.Data is CustomEvent e)
            {
                switch (e.Letter)
                {
                    case 'a':
                        aggregate.ACount++;
                        break;

                    case 'b':
                        aggregate.BCount++;
                        break;

                    case 'c':
                        aggregate.CCount++;
                        break;

                    case 'd':
                        aggregate.DCount++;
                        break;
                }
            }
        }

        session.Store(aggregate);
        return new ValueTask();
    }

}

public class CustomAggregate
{
    public int Id { get; set; }

    public int ACount { get; set; }
    public int BCount { get; set; }
    public int CCount { get; set; }
    public int DCount { get; set; }

    protected bool Equals(CustomAggregate other)
    {
        return Id == other.Id && ACount == other.ACount && BCount == other.BCount && CCount == other.CCount && DCount == other.DCount;
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj))
        {
            return false;
        }

        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj.GetType() != this.GetType())
        {
            return false;
        }

        return Equals((CustomAggregate)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Id, ACount, BCount, CCount, DCount);
    }

    public override string ToString()
    {
        return $"{nameof(Id)}: {Id}, {nameof(ACount)}: {ACount}, {nameof(BCount)}: {BCount}, {nameof(CCount)}: {CCount}, {nameof(DCount)}: {DCount}";
    }
}