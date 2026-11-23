using System;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using StronglyTypedIds;
using Xunit;

namespace EventSourcingTests.Bugs;

public record Bug6147Bumped;

public class Bug6147Counter
{
    public Guid Id { get; set; }
    public int Count { get; set; }

    public void Apply(Bug6147Bumped _) => Count++;
}

public record Bug6148Tick;

public record Bug6148Named(string Code);

[StronglyTypedId(Template.Guid)]
public readonly partial struct Bug6148CounterId;

public class Bug6148TypedCounter
{
    public Bug6148CounterId? Id { get; set; }
    public int Count { get; set; }

    public void Apply(Bug6148Tick _) => Count++;
}

public class Bug6148KeyedCounter
{
    public Guid Id { get; set; }

    [NaturalKey]
    public string? Code { get; set; }

    public int Count { get; set; }

    [NaturalKeySource]
    public void Apply(Bug6148Named e) => Code = e.Code;

    public void Apply(Bug6148Tick _) => Count++;
}

/// <summary>
///     marten#6147: reading an aggregate back with FetchLatest after FetchForWriting and an append folded the pending
///     events onto the instance the aggregate write cache holds for the stream, so the next FetchForWriting applied them
///     again. marten#6148: an Inline aggregate with a strong-typed id read by the stream's Guid. ProjectLatest ejected it
///     from the identity map by the Guid, which the map is not keyed by, so the commit folded the pending events onto
///     the instance ProjectLatest had already folded them onto; with the identity map off, FetchLatest and ProjectLatest
///     threw DocumentIdTypeMismatchException.
/// </summary>
public class Bug_6147_6148_fetch_plan_folds: OneOffConfigurationsContext
{
    [Theory]
    [InlineData(ProjectionLifecycle.Async, false)]
    [InlineData(ProjectionLifecycle.Live, false)]
    [InlineData(ProjectionLifecycle.Inline, false)]
    [InlineData(ProjectionLifecycle.Async, true)]
    [InlineData(ProjectionLifecycle.Live, true)]
    [InlineData(ProjectionLifecycle.Inline, true)]
    public async Task reading_back_after_an_append_does_not_double_count_the_next_fetch_for_writing(
        ProjectionLifecycle lifecycle, bool projectLatest)
    {
        StoreOptions(opts =>
        {
            switch (lifecycle)
            {
                case ProjectionLifecycle.Live:
                    opts.Projections.LiveStreamAggregation<Bug6147Counter>();
                    break;
                case ProjectionLifecycle.Inline:
                    opts.Projections.Snapshot<Bug6147Counter>(SnapshotLifecycle.Inline);
                    break;
                default:
                    opts.Projections.Snapshot<Bug6147Counter>(SnapshotLifecycle.Async);
                    break;
            }

            opts.Events.CacheAggregatesForWriting<Bug6147Counter>();
        });

        var id = theSession.Events.StartStream<Bug6147Counter>(new Bug6147Bumped()).Id;
        await theSession.SaveChangesAsync();

        await using (var session = theStore.LightweightSession())
        {
            var stream = await session.Events.FetchForWriting<Bug6147Counter>(id);
            stream.AppendOne(new Bug6147Bumped());

            if (projectLatest)
            {
                (await session.Events.ProjectLatest<Bug6147Counter>(id))!.Count.ShouldBe(2);
            }
            else
            {
                // Async and Live fold the pending event in here, Inline returns the stored snapshot
                await session.Events.FetchLatest<Bug6147Counter>(id);
            }

            await session.SaveChangesAsync();
        }

        await using var later = theStore.LightweightSession();
        var again = await later.Events.FetchForWriting<Bug6147Counter>(id);
        again.Aggregate!.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task project_latest_on_an_inline_strong_typed_id_aggregate_stores_the_committed_count(
        bool identityMapForAggregates, bool identitySession)
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<Bug6148TypedCounter>(SnapshotLifecycle.Inline);
            opts.Events.UseIdentityMapForAggregates = identityMapForAggregates;
        });

        var id = theSession.Events.StartStream<Bug6148TypedCounter>(new Bug6148Tick()).Id;
        await theSession.SaveChangesAsync();

        await using (var session = identitySession ? theStore.IdentitySession() : theStore.LightweightSession())
        {
            var stream = await session.Events.FetchForWriting<Bug6148TypedCounter>(id);
            stream.AppendOne(new Bug6148Tick());

            (await session.Events.ProjectLatest<Bug6148TypedCounter>(id))!.Count.ShouldBe(2);

            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession();
        (await query.LoadAsync<Bug6148TypedCounter>(new Bug6148CounterId(id)))!.Count.ShouldBe(2);
        (await query.Events.AggregateStreamAsync<Bug6148TypedCounter>(id))!.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task fetch_latest_on_an_inline_strong_typed_id_aggregate(bool identityMapForAggregates)
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<Bug6148TypedCounter>(SnapshotLifecycle.Inline);
            opts.Events.UseIdentityMapForAggregates = identityMapForAggregates;
        });

        var id = theSession.Events.StartStream<Bug6148TypedCounter>(new Bug6148Tick()).Id;
        await theSession.SaveChangesAsync();

        await using var session = theStore.LightweightSession();
        (await session.Events.FetchLatest<Bug6148TypedCounter>(id))!.Count.ShouldBe(1);
        (await session.Events.FetchLatest<Bug6148TypedCounter, Bug6148CounterId>(new Bug6148CounterId(id)))!
            .Count.ShouldBe(1);

        session.Events.Append(id, new Bug6148Tick());
        (await session.Events.ProjectLatest<Bug6148TypedCounter>(id))!.Count.ShouldBe(2);
    }

    /// <summary>
    ///     The natural-key twin of marten#6145: ProjectLatest by a string natural key stored the folded Async
    ///     aggregate, and the daemon then applied the same events to it again.
    /// </summary>
    [Fact]
    public async Task project_latest_by_natural_key_on_an_async_aggregate_leaves_the_snapshot_to_the_daemon()
    {
        StoreOptions(opts => opts.Projections.Snapshot<Bug6148KeyedCounter>(SnapshotLifecycle.Async));

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var id = theSession.Events.StartStream<Bug6148KeyedCounter>(new Bug6148Named("XYZ-6145"), new Bug6148Tick()).Id;
        await theSession.SaveChangesAsync();
        await daemon.WaitForNonStaleData(15.Seconds());

        await using (var session = theStore.LightweightSession())
        {
            var stream = await session.Events.FetchForWriting<Bug6148KeyedCounter>(id);
            stream.AppendOne(new Bug6148Tick());

            (await session.Events.ProjectLatest<Bug6148KeyedCounter>("XYZ-6145"))!.Count.ShouldBe(2);

            await session.SaveChangesAsync();
        }

        await daemon.WaitForNonStaleData(15.Seconds());

        await using var query = theStore.QuerySession();
        (await query.LoadAsync<Bug6148KeyedCounter>(id))!.Count.ShouldBe(2);
        (await query.Events.AggregateStreamAsync<Bug6148KeyedCounter>(id))!.Count.ShouldBe(2);
    }
}
