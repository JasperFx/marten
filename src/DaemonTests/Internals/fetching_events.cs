using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.MultiTenancy;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events;
using Marten.Events.Daemon.Internals;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql.SqlGeneration;
using Xunit;

namespace DaemonTests.Internals;

public class fetching_events: OneOffConfigurationsContext, IAsyncLifetime
{
    private readonly List<ISqlFragment> theFilters = new();
    private readonly EventRange theRange;
    private readonly ShardName theShardName = new("foo", "All", 1);

    public fetching_events()
    {
        theRange = new EventRange(theShardName, 0, 100);
    }

    public Task InitializeAsync()
    {
        return theStore.Advanced.Clean.DeleteAllEventDataAsync();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    internal async Task executeAfterLoadingEvents(Action<IEventStoreOperations> loadEvents)
    {
        loadEvents(theSession.Events);
        await theSession.SaveChangesAsync();

        var fetcher = new EventLoader(theStore, (MartenDatabase)theStore.Tenancy.Default.Database, new AsyncOptions(),
            theFilters.ToArray());

        var results = await fetcher.LoadAsync(
            new EventRequest
            {
                Floor = theRange.SequenceFloor,
                BatchSize = 1000,
                HighWater = 1000,
                Runtime = new NulloDaemonRuntime(),
                Name = theShardName
            },
            CancellationToken.None);

        theRange.Events = results.ToList();
    }


    [Fact]
    public async Task simple_fetch_with_guid_identifiers()
    {
        var stream = Guid.NewGuid();
        await executeAfterLoadingEvents(e =>
        {
            e.Append(stream, new AEvent(), new BEvent(), new CEvent(), new DEvent());
        });

        await theSession.SaveChangesAsync();

        theRange.Events.Count.ShouldBe(4);
        var @event = theRange.Events[0];
        @event.StreamId.ShouldBe(stream);
        @event.Version.ShouldBe(1);
        @event.Data.ShouldBeOfType<AEvent>();
    }

    [Fact]
    public async Task simple_fetch_with_string_identifiers()
    {
        StoreOptions(x => x.Events.StreamIdentity = StreamIdentity.AsString);

        var stream = Guid.NewGuid().ToString();
        await executeAfterLoadingEvents(e =>
        {
            e.Append(stream, new AEvent(), new BEvent(), new CEvent(), new DEvent());
        });

        await theSession.SaveChangesAsync();

        theRange.Events.Count.ShouldBe(4);
        var @event = theRange.Events[0];
        @event.StreamKey.ShouldBe(stream);
        @event.Version.ShouldBe(1);
        @event.Data.ShouldBeOfType<AEvent>();
    }

    [Fact]
    public async Task should_get_the_aggregate_type_name_if_exists()
    {
        await executeAfterLoadingEvents(e =>
        {
            e.Append(Guid.NewGuid(), new AEvent(), new BEvent(), new CEvent(), new DEvent());
            e.StartStream<Letters>(Guid.NewGuid(), new AEvent(), new BEvent(), new CEvent(), new DEvent(),
                new DEvent());
        });

        for (var i = 0; i < 4; i++)
        {
            theRange.Events[i].AggregateTypeName.ShouldBeNull();
        }

        for (var i = 4; i < theRange.Events.Count; i++)
        {
            theRange.Events[i].AggregateTypeName.ShouldBe("letters");
        }
    }

    [Fact]
    public async Task should_get_the_generic_aggregate_type_name_if_exists()
    {
        await executeAfterLoadingEvents(e =>
        {
            e.Append(Guid.NewGuid(), new AEvent(), new BEvent(), new CEvent(), new DEvent());
            e.StartStream<Letters<Value>>(Guid.NewGuid(), new AEvent(), new BEvent(), new CEvent(), new DEvent(),
                new DEvent());
        });

        for (var i = 0; i < 4; i++)
        {
            theRange.Events[i].AggregateTypeName.ShouldBeNull();
        }

        for (var i = 4; i < theRange.Events.Count; i++)
        {
            theRange.Events[i].AggregateTypeName.ShouldBe("Letters<Value>");
        }
    }


    [Fact]
    public async Task filter_on_aggregate_type_name_if_exists()
    {
        theFilters.Add(new AggregateTypeFilter(typeof(Letters), theStore.Events));

        await executeAfterLoadingEvents(e =>
        {
            e.Append(Guid.NewGuid(), new AEvent(), new BEvent(), new CEvent(), new DEvent());
            e.StartStream<Letters>(Guid.NewGuid(), new AEvent(), new BEvent(), new CEvent(), new DEvent(),
                new DEvent());
        });

        theRange.Events.Count.ShouldBe(5);
        foreach (var @event in theRange.Events) @event.AggregateTypeName.ShouldBe("letters");
    }

    [Fact]
    public async Task filter_on_generic_aggregate_type_name_if_exists()
    {
        theFilters.Add(new AggregateTypeFilter(typeof(Letters<Value>), theStore.Events));

        await executeAfterLoadingEvents(e =>
        {
            e.Append(Guid.NewGuid(), new AEvent(), new BEvent(), new CEvent(), new DEvent());
            e.StartStream<Letters<Value>>(Guid.NewGuid(), new AEvent(), new BEvent(), new CEvent(), new DEvent(),
                new DEvent());
        });

        theRange.Events.Count.ShouldBe(5);
        foreach (var @event in theRange.Events) @event.AggregateTypeName.ShouldBe("Letters<Value>");
    }
}

public class Letters
{
    public int ACount { get; set; }
    public int BCount { get; set; }
    public int CCount { get; set; }
    public int DCount { get; set; }
}

public class Letters<T>
    where T : Value
{
    public T ACount { get; set; }
    public T BCount { get; set; }
    public T CCount { get; set; }
    public T DCount { get; set; }
}

public record Value(int Conut);
