using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using EventSourcingTests.Bugs;
using JasperFx.Events;
using JasperFx.Events.Grouping;
using JasperFx.Events.Projections;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Internal.Sessions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace DaemonTests;

public class custom_aggregation_in_async_daemon : OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;

    public custom_aggregation_in_async_daemon(ITestOutputHelper output)
    {
        _output = output;
    }

    private void appendCustomEvent(int number, char letter)
    {
        theSession.Events.Append(Guid.NewGuid(), new CustomEvent(number, letter));
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

        await theStore.Advanced.Clean.DeleteAllDocumentsAsync();
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();

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

        await theSession.SaveChangesAsync();

        using var daemon = await theStore.BuildProjectionDaemonAsync(logger:new TestLogger<IProjection>(_output));
        await daemon.StartAllAsync();

        await  daemon.Tracker.WaitForShardState("Custom:All", 11);

        var agg1 = await theSession.LoadAsync<CustomAggregate>(1);
        agg1
            .ShouldBe(new CustomAggregate{Id = 1, ACount = 4, BCount = 1, CCount = 1, DCount = 1});

        (await theSession.LoadAsync<CustomAggregate>(2))
            .ShouldBe(new CustomAggregate{Id = 2, ACount = 2, BCount = 0, CCount = 0, DCount = 0});

        (await theSession.LoadAsync<CustomAggregate>(3))
            .ShouldBe(new CustomAggregate{Id = 3, ACount = 0, BCount = 1, CCount = 0, DCount = 1});

    }

    [Fact]
    public async Task run_end_to_end_with_caching()
    {
        StoreOptions(opts =>
        {
            var myCustomAggregation = new MyCustomProjection{Options = {CacheLimitPerTenant = 100}};
            opts.Projections.Add(myCustomAggregation, ProjectionLifecycle.Async);
            opts.Logger(new TestOutputMartenLogger(_output));
        });

        await theStore.Advanced.Clean.DeleteAllDocumentsAsync();
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();

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

        await theSession.SaveChangesAsync();

        using var daemon = await theStore.BuildProjectionDaemonAsync(logger:new TestLogger<IProjection>(_output));
        await daemon.StartAllAsync();

        await  daemon.Tracker.WaitForShardState("Custom:All", 11);

        var agg1 = await theSession.LoadAsync<CustomAggregate>(1);
        agg1
            .ShouldBe(new CustomAggregate{Id = 1, ACount = 4, BCount = 1, CCount = 1, DCount = 1});

        (await theSession.LoadAsync<CustomAggregate>(2))
            .ShouldBe(new CustomAggregate{Id = 2, ACount = 2, BCount = 0, CCount = 0, DCount = 0});

        (await theSession.LoadAsync<CustomAggregate>(3))
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


public class MyCustomProjection: MultiStreamProjection<CustomAggregate, int>
{
    public MyCustomProjection()
    {
        ProjectionName = "Custom";

        Identity<INumbered>(x => x.Number);
    }

    public override CustomAggregate Evolve(CustomAggregate snapshot, int id, IEvent e)
    {
        if (e.Data is CustomEvent ce)
        {
            snapshot ??= new();

            switch (ce.Letter)
            {
                case 'a':
                    snapshot.ACount++;
                    break;

                case 'b':
                    snapshot.BCount++;
                    break;

                case 'c':
                    snapshot.CCount++;
                    break;

                case 'd':
                    snapshot.DCount++;
                    break;
            }
        }

        return snapshot;
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
