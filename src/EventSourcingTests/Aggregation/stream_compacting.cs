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
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Metadata;
using Marten.Testing.Harness;
using NSubstitute;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Aggregation;

public class stream_compacting : OneOffConfigurationsContext
{
    public AEvent A() => new AEvent();
    public BEvent B() => new BEvent();
    public CEvent C() => new CEvent();
    public DEvent D() => new DEvent();

    [Fact]
    public async Task start_with_self_aggregate()
    {
        var streamId = Guid.NewGuid();
        var starter = new Compacted<Letters>(new Letters { ACount = 3, BCount = 11, Version = 5, Id = streamId },
            Guid.NewGuid(), "");
        theSession.Events.StartStream<Letters>(streamId, starter);
        await theSession.SaveChangesAsync();

        var aggregate = await theSession.Events.AggregateStreamAsync<Letters>(streamId);

        aggregate.ACount.ShouldBe(3);
        aggregate.BCount.ShouldBe(11);
    }

    [Fact]
    public async Task start_and_continue_with_self_aggregate()
    {
        var streamId = Guid.NewGuid();
        var starter = new Compacted<Letters>(new Letters { ACount = 3, BCount = 11, Version = 5, Id = streamId },
            Guid.NewGuid(), "");
        theSession.Events.StartStream<Letters>(streamId, starter, new DEvent(), new DEvent(), new DEvent());
        await theSession.SaveChangesAsync();

        var aggregate = await theSession.Events.AggregateStreamAsync<Letters>(streamId);

        aggregate.ACount.ShouldBe(3);
        aggregate.BCount.ShouldBe(11);
        aggregate.DCount.ShouldBe(3);
    }

    [Fact]
    public async Task compacted_in_the_middle()
    {
        var streamId = Guid.NewGuid();
        var starter = new Compacted<Letters>(new Letters { ACount = 3, BCount = 11, Version = 5, Id = streamId },
            Guid.NewGuid(), "");
        theSession.Events.StartStream<Letters>(streamId, new AEvent(),  new DEvent(), new DEvent(), new DEvent(),starter, new DEvent(), new DEvent(), new DEvent());
        await theSession.SaveChangesAsync();

        var aggregate = await theSession.Events.AggregateStreamAsync<Letters>(streamId);

        // Nothing before the Compacted should matter
        aggregate.ACount.ShouldBe(3);
        aggregate.BCount.ShouldBe(11);
        aggregate.DCount.ShouldBe(3);
    }

    [Fact]
    public async Task compacted_in_the_middle_build_through_async_daemon()
    {
        StoreOptions(opts => opts.Projections.Snapshot<Letters>(SnapshotLifecycle.Async));

        var streamId = Guid.NewGuid();
        var starter = new Compacted<Letters>(new Letters { ACount = 3, BCount = 11, Version = 5, Id = streamId },
            Guid.NewGuid(), "");
        theSession.Events.StartStream<Letters>(streamId, new AEvent(),  new DEvent(), new DEvent(), new DEvent(),starter, new DEvent(), new DEvent(), new DEvent());
        await theSession.SaveChangesAsync();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(5.Seconds());

        var aggregate = await theSession.LoadAsync<Letters>(streamId);

        // Nothing before the Compacted should matter
        aggregate.ACount.ShouldBe(3);
        aggregate.BCount.ShouldBe(11);
        aggregate.DCount.ShouldBe(3);
    }

    [Fact]
    public async Task start_with_sync_evolve()
    {
        StoreOptions(opts => opts.Projections.Add<LetterCountsProjection1>(ProjectionLifecycle.Inline));

        var streamId = Guid.NewGuid();
        var starter = new Compacted<LetterCounts>(new LetterCounts { CCount = 3, BCount = 11, Version = 5, Id = streamId },
            Guid.NewGuid(), "");
        theSession.Events.StartStream<LetterCounts>(streamId, starter);
        await theSession.SaveChangesAsync();

        var aggregate = await theSession.Events.AggregateStreamAsync<LetterCounts>(streamId);

        aggregate.CCount.ShouldBe(3);
        aggregate.BCount.ShouldBe(11);
    }

    [Fact]
    public async Task start_with_async_evolve()
    {
        StoreOptions(opts => opts.Projections.Add<LetterCountsProjection2>(ProjectionLifecycle.Inline));

        var streamId = Guid.NewGuid();
        var starter = new Compacted<LetterCounts>(new LetterCounts { CCount = 3, BCount = 14, Version = 5, Id = streamId },
            Guid.NewGuid(), "");
        theSession.Events.StartStream<LetterCounts>(streamId, starter);
        await theSession.SaveChangesAsync();

        var aggregate = await theSession.Events.AggregateStreamAsync<LetterCounts>(streamId);

        aggregate.CCount.ShouldBe(3);
        aggregate.BCount.ShouldBe(14);

    }

    [Fact]
    public async Task start_with_sync_determine_action()
    {
        StoreOptions(opts => opts.Projections.Add<LetterCountsProjection3>(ProjectionLifecycle.Inline));

        var streamId = Guid.NewGuid();
        var starter = new Compacted<LetterCounts>(new LetterCounts { CCount = 3, BCount = 14, Version = 5, Id = streamId },
            Guid.NewGuid(), "");
        theSession.Events.StartStream<LetterCounts>(streamId, starter);
        await theSession.SaveChangesAsync();

        var aggregate = await theSession.Events.AggregateStreamAsync<LetterCounts>(streamId);

        aggregate.CCount.ShouldBe(3);
        aggregate.BCount.ShouldBe(14);

    }

    [Fact]
    public async Task start_with_async_determine_action()
    {
        StoreOptions(opts => opts.Projections.Add<LetterCountsProjection4>(ProjectionLifecycle.Inline));

        var streamId = Guid.NewGuid();
        var starter = new Compacted<LetterCounts>(new LetterCounts { CCount = 3, BCount = 14, Version = 5, Id = streamId },
            Guid.NewGuid(), "");
        theSession.Events.StartStream<LetterCounts>(streamId, starter);
        await theSession.SaveChangesAsync();

        var aggregate = await theSession.Events.AggregateStreamAsync<LetterCounts>(streamId);

        aggregate.CCount.ShouldBe(3);
        aggregate.BCount.ShouldBe(14);

    }

    [Fact]
    public async Task end_to_end_guid_identification_at_latest()
    {
        StoreOptions(opts => opts.Projections.Snapshot<Letters>(SnapshotLifecycle.Inline));

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<Letters>(streamId, A(), B(), A(), C(), C(), D(), D(), A(), A());
        await theSession.SaveChangesAsync();

        await theSession.Events.CompactStreamAsync<Letters>(streamId);
        await theSession.SaveChangesAsync();

        var state = await theSession.Events.FetchStreamStateAsync(streamId);
        state.Version.ShouldBe(9);

        var events = await theSession.Events.FetchStreamAsync(streamId);
        var compacted = events.Single().ShouldBeOfType < Event<Compacted<Letters>>>();
        compacted.Version.ShouldBe(9);
        compacted.Data.Snapshot.ACount.ShouldBe(4);
        compacted.Data.Snapshot.BCount.ShouldBe(1);
        compacted.Data.Snapshot.CCount.ShouldBe(2);
        compacted.Data.Snapshot.DCount.ShouldBe(2);

        var aggregated = await theSession.Events.AggregateStreamAsync<Letters>(streamId);
        aggregated.ACount.ShouldBe(4);
        aggregated.BCount.ShouldBe(1);
        aggregated.CCount.ShouldBe(2);
        aggregated.DCount.ShouldBe(2);
    }

    [Fact]
    public async Task end_to_end_guid_identification_at_specified_version()
    {
        StoreOptions(opts => opts.Projections.Snapshot<Letters>(SnapshotLifecycle.Inline));

        var archiver = new StubEventsArchiver();

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<Letters>(streamId, A(), B(), A(), C(), C(), D(), D(), A(), A());
        await theSession.SaveChangesAsync();

        await theSession.Events.CompactStreamAsync<Letters>(streamId, x =>
        {
            x.Version = 5;
            x.Archiver = archiver;
        });
        await theSession.SaveChangesAsync();

        archiver.LastRequest.ShouldNotBeNull();

        // 5 events should be archived, up to the point where w
        archiver.LastEvents.Count.ShouldBe(5);


        var state = await theSession.Events.FetchStreamStateAsync(streamId);
        state.Version.ShouldBe(9);

        var events = await theSession.Events.FetchStreamAsync(streamId);
        events.Count.ShouldBe(5);

        var compacted = events.First().ShouldBeOfType < Event<Compacted<Letters>>>();
        compacted.Version.ShouldBe(5);
        compacted.Data.Snapshot.ACount.ShouldBe(2);
        compacted.Data.Snapshot.BCount.ShouldBe(1);
        compacted.Data.Snapshot.CCount.ShouldBe(2);
        compacted.Data.Snapshot.DCount.ShouldBe(0);

        var aggregated = await theSession.Events.AggregateStreamAsync<Letters>(streamId);
        aggregated.ACount.ShouldBe(4);
        aggregated.BCount.ShouldBe(1);
        aggregated.CCount.ShouldBe(2);
        aggregated.DCount.ShouldBe(2);
    }


    [Fact]
    public async Task end_to_end_string_identification_at_latest()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Projections.Add<LetterCountsByStringProjection>(ProjectionLifecycle.Inline);
        });

        var streamId = Guid.NewGuid().ToString();
        theSession.Events.StartStream<LetterCountsByString>(streamId, A(), B(), A(), C(), C(), D(), D(), A(), A());
        await theSession.SaveChangesAsync();

        await theSession.Events.CompactStreamAsync<LetterCountsByString>(streamId);
        await theSession.SaveChangesAsync();

        var state = await theSession.Events.FetchStreamStateAsync(streamId);
        state.Version.ShouldBe(9);

        var events = await theSession.Events.FetchStreamAsync(streamId);
        var compacted = events.Single().ShouldBeOfType < Event<Compacted<LetterCountsByString>>>();
        compacted.Version.ShouldBe(9);
        compacted.Data.Snapshot.ACount.ShouldBe(4);
        compacted.Data.Snapshot.BCount.ShouldBe(1);
        compacted.Data.Snapshot.CCount.ShouldBe(2);
        compacted.Data.Snapshot.DCount.ShouldBe(2);

        var aggregated = await theSession.Events.AggregateStreamAsync<LetterCountsByString>(streamId);
        aggregated.ACount.ShouldBe(4);
        aggregated.BCount.ShouldBe(1);
        aggregated.CCount.ShouldBe(2);
        aggregated.DCount.ShouldBe(2);
    }

    [Fact]
    public async Task end_to_end_string_identification_at_specified_version()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Projections.Add<LetterCountsByStringProjection>(ProjectionLifecycle.Inline);
        });

        var streamId = Guid.NewGuid().ToString();
        theSession.Events.StartStream<LetterCountsByString>(streamId, A(), B(), A(), C(), C(), D(), D(), A(), A());
        await theSession.SaveChangesAsync();

        await theSession.Events.CompactStreamAsync<LetterCountsByString>(streamId, x => x.Version = 5);
        await theSession.SaveChangesAsync();

        var state = await theSession.Events.FetchStreamStateAsync(streamId);
        state.Version.ShouldBe(9);

        var events = await theSession.Events.FetchStreamAsync(streamId);
        events.Count.ShouldBe(5);

        var compacted = events.First().ShouldBeOfType < Event<Compacted<LetterCountsByString>>>();
        compacted.Version.ShouldBe(5);
        compacted.Data.Snapshot.ACount.ShouldBe(2);
        compacted.Data.Snapshot.BCount.ShouldBe(1);
        compacted.Data.Snapshot.CCount.ShouldBe(2);
        compacted.Data.Snapshot.DCount.ShouldBe(0);

        var aggregated = await theSession.Events.AggregateStreamAsync<LetterCountsByString>(streamId);
        aggregated.ACount.ShouldBe(4);
        aggregated.BCount.ShouldBe(1);
        aggregated.CCount.ShouldBe(2);
        aggregated.DCount.ShouldBe(2);
    }
}


public class Letters : IRevisioned
{
    public Guid Id { get; set; }

    public void Apply(AEvent _) => ACount++;
    public void Apply(BEvent _) => BCount++;
    public void Apply(CEvent _) => CCount++;
    public void Apply(DEvent _) => DCount++;

    public int ACount { get; set; }
    public int BCount { get; set; }
    public int CCount { get; set; }
    public int DCount { get; set; }
    public int Version { get; set; }
}

public class LetterCounts: IRevisioned
{
    public Guid Id { get; set; }
    public int ACount { get; set; }
    public int BCount { get; set; }
    public int CCount { get; set; }
    public int DCount { get; set; }
    public int Version { get; set; }
}

public class LetterCountsByString: IRevisioned
{
    public string Id { get; set; }
    public int ACount { get; set; }
    public int BCount { get; set; }
    public int CCount { get; set; }
    public int DCount { get; set; }
    public int Version { get; set; }
}

public class LetterCountsByStringProjection: SingleStreamProjection<LetterCountsByString, string>
{
    public override LetterCountsByString Evolve(LetterCountsByString snapshot, string id, IEvent e)
    {
        snapshot ??= new LetterCountsByString { Id = id };

        switch (e.Data)
        {
            case AEvent _:
                snapshot.ACount++;
                break;

            case BEvent _:
                snapshot.BCount++;
                break;

            case CEvent _:
                snapshot.CCount++;
                break;

            case DEvent _:
                snapshot.DCount++;
                break;
        }

        return snapshot;
    }
}

public class LetterCountsProjection1: SingleStreamProjection<LetterCounts, Guid>
{
    public override LetterCounts Evolve(LetterCounts snapshot, Guid id, IEvent e)
    {
        switch (e.Data)
        {
            case AEvent _:
                snapshot.ACount++;
                break;

            case BEvent _:
                snapshot.BCount++;
                break;

            case CEvent _:
                snapshot.CCount++;
                break;

            case DEvent _:
                snapshot.DCount++;
                break;
        }

        return snapshot;
    }
}

public class LetterCountsProjection2: SingleStreamProjection<LetterCounts, Guid>
{
    public override ValueTask<LetterCounts> EvolveAsync(LetterCounts snapshot, Guid id, IQuerySession session, IEvent e, CancellationToken cancellation)
    {
        switch (e.Data)
        {
            case AEvent _:
                snapshot.ACount++;
                break;

            case BEvent _:
                snapshot.BCount++;
                break;

            case CEvent _:
                snapshot.CCount++;
                break;

            case DEvent _:
                snapshot.DCount++;
                break;
        }

        return new ValueTask<LetterCounts>(snapshot);
    }
}

public class LetterCountsProjection3: SingleStreamProjection<LetterCounts, Guid>
{
    public override (LetterCounts, ActionType) DetermineAction(LetterCounts snapshot, Guid identity, IReadOnlyList<IEvent> events)
    {
        foreach (var e in events)
        {
            switch (e.Data)
            {
                case AEvent _:
                    snapshot.ACount++;
                    break;

                case BEvent _:
                    snapshot.BCount++;
                    break;

                case CEvent _:
                    snapshot.CCount++;
                    break;

                case DEvent _:
                    snapshot.DCount++;
                    break;
            }
        }

        return (snapshot, ActionType.Store);
    }
}

public class LetterCountsProjection4: SingleStreamProjection<LetterCounts, Guid>
{
    public override ValueTask<(LetterCounts, ActionType)> DetermineActionAsync(IQuerySession session, LetterCounts snapshot, Guid identity,
        IIdentitySetter<LetterCounts, Guid> identitySetter, IReadOnlyList<IEvent> events, CancellationToken cancellation)
    {
        foreach (var e in events)
        {
            switch (e.Data)
            {
                case AEvent _:
                    snapshot.ACount++;
                    break;

                case BEvent _:
                    snapshot.BCount++;
                    break;

                case CEvent _:
                    snapshot.CCount++;
                    break;

                case DEvent _:
                    snapshot.DCount++;
                    break;
            }
        }

        return new ValueTask<(LetterCounts, ActionType)>((snapshot, ActionType.Store));
    }
}

public class StubEventsArchiver: IEventsArchiver
{
    public Task MaybeArchiveAsync<T>(IDocumentOperations operations, StreamCompactingRequest<T> request, IReadOnlyList<IEvent> events,
        CancellationToken cancellation)
    {
        LastRequest = request;
        LastEvents = events;
        return Task.CompletedTask;
    }

    public IReadOnlyList<IEvent> LastEvents { get; set; }

    public object LastRequest { get; set; }
}
