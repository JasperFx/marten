using System;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections;
using Marten.Services;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

public record CounterStarted;

public record CounterBumped;

public record CounterReplaced;

public class MutableCounter
{
    public Guid Id { get; set; }
    public int Count { get; set; }

    public static MutableCounter Create(CounterStarted _) => new();

    public void Apply(CounterBumped _) => Count++;

    public MutableCounter Apply(CounterReplaced _) => new() { Id = Id, Count = Count };
}

public record ImmutableCounter(Guid Id, int Count)
{
    public static ImmutableCounter Create(IEvent<CounterStarted> e) => new(e.StreamId, 0);

    public ImmutableCounter Apply(CounterBumped _) => this with { Count = Count + 1 };

    public ImmutableCounter Apply(CounterReplaced _) => this with { };
}

public static class ReadingAfterFetchForWritingCases
{
    public static TheoryData<ProjectionLifecycle, bool> All => new()
    {
        { ProjectionLifecycle.Live, true },
        { ProjectionLifecycle.Inline, true },
        { ProjectionLifecycle.Async, true },
        { ProjectionLifecycle.Live, false },
        { ProjectionLifecycle.Inline, false },
        { ProjectionLifecycle.Async, false },
    };

    public static TheoryData<ProjectionLifecycle, bool, DocumentTracking> AllWithTracking
    {
        get
        {
            var data = new TheoryData<ProjectionLifecycle, bool, DocumentTracking>();
            foreach (var lifecycle in new[] { ProjectionLifecycle.Live, ProjectionLifecycle.Inline, ProjectionLifecycle.Async })
            foreach (var useIdentityMap in new[] { true, false })
            foreach (var tracking in new[] { DocumentTracking.None, DocumentTracking.IdentityOnly, DocumentTracking.DirtyTracking })
            {
                data.Add(lifecycle, useIdentityMap, tracking);
            }

            return data;
        }
    }
}

public class reading_after_fetch_for_writing_a_mutable_aggregate()
    : reading_after_fetch_for_writing_in_the_same_session<MutableCounter>(x => x.Count);

public class reading_after_fetch_for_writing_an_immutable_aggregate()
    : reading_after_fetch_for_writing_in_the_same_session<ImmutableCounter>(x => x.Count);

public abstract class reading_after_fetch_for_writing_in_the_same_session<T>(Func<T, int> countOf)
    : OneOffConfigurationsContext where T : class
{
    [Theory]
    [MemberData(nameof(ReadingAfterFetchForWritingCases.All), MemberType = typeof(ReadingAfterFetchForWritingCases))]
    public async Task project_latest_applies_the_appended_event_once(ProjectionLifecycle lifecycle,
        bool useIdentityMap)
    {
        var streamId = await startCountedOnce(lifecycle, useIdentityMap);

        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<T>(streamId);
        stream.AppendOne(new CounterBumped());

        countOf((await session.Events.ProjectLatest<T>(streamId))!).ShouldBe(2);
    }

    [Theory]
    [MemberData(nameof(ReadingAfterFetchForWritingCases.All), MemberType = typeof(ReadingAfterFetchForWritingCases))]
    public async Task project_latest_answers_the_same_when_called_twice(ProjectionLifecycle lifecycle,
        bool useIdentityMap)
    {
        var streamId = await startCountedOnce(lifecycle, useIdentityMap);

        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<T>(streamId);
        stream.AppendOne(new CounterBumped());

        countOf((await session.Events.ProjectLatest<T>(streamId))!).ShouldBe(2);
        countOf((await session.Events.ProjectLatest<T>(streamId))!).ShouldBe(2);
    }

    [Theory]
    [MemberData(nameof(ReadingAfterFetchForWritingCases.All), MemberType = typeof(ReadingAfterFetchForWritingCases))]
    public async Task project_latest_after_fetch_latest_applies_the_appended_event_once(
        ProjectionLifecycle lifecycle, bool useIdentityMap)
    {
        var streamId = await startCountedOnce(lifecycle, useIdentityMap);

        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<T>(streamId);
        stream.AppendOne(new CounterBumped());

        await session.Events.FetchLatest<T>(streamId);

        countOf((await session.Events.ProjectLatest<T>(streamId))!).ShouldBe(2);
    }

    [Theory]
    [MemberData(nameof(ReadingAfterFetchForWritingCases.All), MemberType = typeof(ReadingAfterFetchForWritingCases))]
    public async Task committing_after_project_latest_leaves_the_aggregate_counted_once(
        ProjectionLifecycle lifecycle, bool useIdentityMap)
    {
        var streamId = await startCountedOnce(lifecycle, useIdentityMap);

        await using (var session = theStore.LightweightSession())
        {
            var stream = await session.Events.FetchForWriting<T>(streamId);
            stream.AppendOne(new CounterBumped());

            await session.Events.ProjectLatest<T>(streamId);
            await session.SaveChangesAsync();

            countOf((await session.Events.FetchLatest<T>(streamId))!).ShouldBe(2);
        }

        await using var query = theStore.LightweightSession();
        countOf((await query.Events.FetchLatest<T>(streamId))!).ShouldBe(2);
    }

    [Theory]
    [MemberData(nameof(ReadingAfterFetchForWritingCases.All), MemberType = typeof(ReadingAfterFetchForWritingCases))]
    public async Task fetch_latest_answers_the_same_when_called_twice_after_the_commit(
        ProjectionLifecycle lifecycle, bool useIdentityMap)
    {
        var streamId = await startCountedOnce(lifecycle, useIdentityMap);

        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<T>(streamId);
        stream.AppendOne(new CounterBumped());
        await session.SaveChangesAsync();

        countOf((await session.Events.FetchLatest<T>(streamId))!).ShouldBe(2);
        countOf((await session.Events.FetchLatest<T>(streamId))!).ShouldBe(2);
    }

    [Theory]
    [MemberData(nameof(ReadingAfterFetchForWritingCases.All), MemberType = typeof(ReadingAfterFetchForWritingCases))]
    public async Task fetch_latest_answers_the_same_twice_when_an_event_replaces_the_aggregate(
        ProjectionLifecycle lifecycle, bool useIdentityMap)
    {
        var streamId = await startCountedOnce(lifecycle, useIdentityMap);

        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<T>(streamId);
        stream.AppendMany(new CounterBumped(), new CounterReplaced());
        await session.SaveChangesAsync();

        countOf((await session.Events.FetchLatest<T>(streamId))!).ShouldBe(2);
        countOf((await session.Events.FetchLatest<T>(streamId))!).ShouldBe(2);
    }

    [Theory]
    [MemberData(nameof(ReadingAfterFetchForWritingCases.All), MemberType = typeof(ReadingAfterFetchForWritingCases))]
    public async Task project_latest_applies_an_event_appended_after_the_commit(ProjectionLifecycle lifecycle,
        bool useIdentityMap)
    {
        var streamId = await startCountedOnce(lifecycle, useIdentityMap);

        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<T>(streamId);
        stream.AppendOne(new CounterBumped());
        await session.SaveChangesAsync();

        session.Events.Append(streamId, new CounterBumped());

        countOf((await session.Events.ProjectLatest<T>(streamId))!).ShouldBe(3);
    }

    // #6106: on an IdentitySession with UseIdentityMapForAggregates off, FetchForWriting put the Inline snapshot
    // in the session's identity map, the commit's projection wrote the new snapshot without touching that entry,
    // and every FetchLatest after the commit answered the snapshot from before it.
    [Theory]
    [MemberData(nameof(ReadingAfterFetchForWritingCases.AllWithTracking), MemberType = typeof(ReadingAfterFetchForWritingCases))]
    public async Task fetch_latest_after_the_commit_returns_the_committed_aggregate(ProjectionLifecycle lifecycle,
        bool useIdentityMap, DocumentTracking tracking)
    {
        var streamId = await startCountedOnce(lifecycle, useIdentityMap);

        await using var session = theStore.OpenSession(new SessionOptions { Tracking = tracking });
        var stream = await session.Events.FetchForWriting<T>(streamId);
        stream.AppendOne(new CounterBumped());
        await session.SaveChangesAsync();

        countOf((await session.Events.FetchLatest<T>(streamId))!).ShouldBe(2);
        countOf((await session.Events.FetchLatest<T>(streamId))!).ShouldBe(2);
    }

    private async Task<Guid> startCountedOnce(ProjectionLifecycle lifecycle, bool useIdentityMap)
    {
        StoreOptions(opts =>
        {
            opts.Events.UseIdentityMapForAggregates = useIdentityMap;

            switch (lifecycle)
            {
                case ProjectionLifecycle.Live:
                    opts.Projections.LiveStreamAggregation<T>();
                    break;
                case ProjectionLifecycle.Inline:
                    opts.Projections.Snapshot<T>(SnapshotLifecycle.Inline);
                    break;
                case ProjectionLifecycle.Async:
                    opts.Projections.Snapshot<T>(SnapshotLifecycle.Async);
                    break;
            }
        });

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<T>(streamId, new CounterStarted(), new CounterBumped());
        await theSession.SaveChangesAsync();

        return streamId;
    }
}
