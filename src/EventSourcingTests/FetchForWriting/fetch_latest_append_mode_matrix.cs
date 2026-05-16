using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

// Coverage matrix: exercises FetchLatest across all three EventAppendMode values
// (Rich, Quick, QuickWithServerTimestamps) and the three projection lifecycles
// (Live, Inline, Async). The existing per-lifecycle test classes implicitly use
// the V9 default (QuickWithServerTimestamps); this file proves the public
// surface still behaves the same under every supported AppendMode.
public class fetch_latest_append_mode_matrix : OneOffConfigurationsContext
{
    public static IEnumerable<object[]> AllAppendModes()
    {
        yield return new object[] { EventAppendMode.Rich };
        yield return new object[] { EventAppendMode.Quick };
        yield return new object[] { EventAppendMode.QuickWithServerTimestamps };
    }

    [Theory]
    [MemberData(nameof(AllAppendModes))]
    public async Task live_aggregate_fetches_latest(EventAppendMode appendMode)
    {
        StoreOptions(opts =>
        {
            opts.Events.AppendMode = appendMode;
            opts.Projections.LiveStreamAggregation<SimpleAggregate>();
        });

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SimpleAggregate>(streamId,
            new AEvent(), new BEvent(), new BEvent(), new CEvent(), new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        await using var query = theStore.LightweightSession();
        var aggregate = await query.Events.FetchLatest<SimpleAggregate>(streamId);

        aggregate.ShouldNotBeNull();
        aggregate.ACount.ShouldBe(1);
        aggregate.BCount.ShouldBe(2);
        aggregate.CCount.ShouldBe(3);
    }

    [Theory]
    [MemberData(nameof(AllAppendModes))]
    public async Task inline_aggregate_fetches_latest(EventAppendMode appendMode)
    {
        StoreOptions(opts =>
        {
            opts.Events.AppendMode = appendMode;
            opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Inline);
        });

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SimpleAggregate>(streamId,
            new AEvent(), new BEvent(), new BEvent(), new CEvent(), new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        await using var query = theStore.LightweightSession();
        var aggregate = await query.Events.FetchLatest<SimpleAggregate>(streamId);

        aggregate.ShouldNotBeNull();
        aggregate.ACount.ShouldBe(1);
        aggregate.BCount.ShouldBe(2);
        aggregate.CCount.ShouldBe(3);
    }

    [Theory]
    [MemberData(nameof(AllAppendModes))]
    public async Task async_aggregate_fetches_latest(EventAppendMode appendMode)
    {
        StoreOptions(opts =>
        {
            opts.Events.AppendMode = appendMode;
            opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async);
        });

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SimpleAggregate>(streamId,
            new AEvent(), new BEvent(), new BEvent(), new CEvent(), new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        // FetchLatest on an async projection picks up the latest snapshot plus any
        // events that haven't been processed by the daemon yet — i.e. it returns the
        // up-to-date aggregate regardless of daemon progress.
        var aggregate = await theSession.Events.FetchLatest<SimpleAggregate>(streamId);

        aggregate.ShouldNotBeNull();
        aggregate.ACount.ShouldBe(1);
        aggregate.BCount.ShouldBe(2);
        aggregate.CCount.ShouldBe(3);
    }

    [Theory]
    [MemberData(nameof(AllAppendModes))]
    public async Task live_aggregate_fetches_latest_with_string_identifier(EventAppendMode appendMode)
    {
        StoreOptions(opts =>
        {
            opts.Events.AppendMode = appendMode;
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Projections.LiveStreamAggregation<SimpleAggregateAsString>();
        });

        var streamId = Guid.NewGuid().ToString();
        theSession.Events.StartStream<SimpleAggregateAsString>(streamId,
            new AEvent(), new BEvent(), new BEvent(), new CEvent(), new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        await using var query = theStore.LightweightSession();
        var aggregate = await query.Events.FetchLatest<SimpleAggregateAsString>(streamId);

        aggregate.ShouldNotBeNull();
        aggregate.ACount.ShouldBe(1);
        aggregate.BCount.ShouldBe(2);
        aggregate.CCount.ShouldBe(3);
    }

    [Theory]
    [MemberData(nameof(AllAppendModes))]
    public async Task inline_aggregate_fetches_latest_with_string_identifier(EventAppendMode appendMode)
    {
        StoreOptions(opts =>
        {
            opts.Events.AppendMode = appendMode;
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Projections.Snapshot<SimpleAggregateAsString>(SnapshotLifecycle.Inline);
        });

        var streamId = Guid.NewGuid().ToString();
        theSession.Events.StartStream<SimpleAggregateAsString>(streamId,
            new AEvent(), new BEvent(), new BEvent(), new CEvent(), new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        await using var query = theStore.LightweightSession();
        var aggregate = await query.Events.FetchLatest<SimpleAggregateAsString>(streamId);

        aggregate.ShouldNotBeNull();
        aggregate.ACount.ShouldBe(1);
        aggregate.BCount.ShouldBe(2);
        aggregate.CCount.ShouldBe(3);
    }

    [Theory]
    [MemberData(nameof(AllAppendModes))]
    public async Task inline_aggregate_fetches_latest_after_appending_more_events(EventAppendMode appendMode)
    {
        StoreOptions(opts =>
        {
            opts.Events.AppendMode = appendMode;
            opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Inline);
        });

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent());
        await theSession.SaveChangesAsync();

        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(streamId, new CEvent(), new CEvent());
            await session.SaveChangesAsync();
        }

        await using var query = theStore.LightweightSession();
        var aggregate = await query.Events.FetchLatest<SimpleAggregate>(streamId);

        aggregate.ShouldNotBeNull();
        aggregate.ACount.ShouldBe(1);
        aggregate.BCount.ShouldBe(1);
        aggregate.CCount.ShouldBe(2);
    }
}
