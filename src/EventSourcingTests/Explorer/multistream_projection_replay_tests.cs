#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Descriptors;
using JasperFx.Events;
using JasperFx.Events.Grouping;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Explorer;

#region multi-stream sample types

public record DepositMade(Guid AccountId, int Amount);
public record AccountClosed(Guid AccountId);

public class AccountBalance
{
    public Guid Id { get; set; }
    public int Balance { get; set; }
}

// A genuine multi-stream projection: it fans out by AccountId (carried on the event),
// NOT by stream, so a single flat event list spanning multiple streams must produce one
// aggregate per account. ShouldDelete removes the aggregate — a reflection-Apply loop would
// silently ignore AccountClosed and leave stale state, so this exercises the real fold path.
public partial class AccountBalanceProjection: MultiStreamProjection<AccountBalance, Guid>
{
    public AccountBalanceProjection()
    {
        Identity<DepositMade>(e => e.AccountId);
        Identity<AccountClosed>(e => e.AccountId);
    }

    public void Apply(DepositMade e, AccountBalance a) => a.Balance += e.Amount;

    public bool ShouldDelete(AccountClosed e) => true;
}

// Enrichment-dependent projection: the account -> owner mapping lives in a persisted
// AccountOwner document, read from the live session during grouping (EnrichEventsAsync).
// A sessionless reflection replay could never produce owner-keyed aggregates.
public record PointsEarned(Guid AccountId, int Points);

public class AccountOwner
{
    public Guid Id { get; set; } // account id
    public Guid OwnerId { get; set; }
}

public class OwnerPoints
{
    public Guid Id { get; set; }
    public int Total { get; set; }
}

public partial class OwnerPointsProjection: MultiStreamProjection<OwnerPoints, Guid>
{
    public OwnerPointsProjection()
    {
        CustomGrouping(new Grouper());
    }

    public void Apply(PointsEarned e, OwnerPoints agg) => agg.Total += e.Points;

    public class Grouper: IAggregateGrouper<Guid>
    {
        public async Task Group(IQuerySession session, IReadOnlyList<IEvent> events, IEventGrouping<Guid> grouping)
        {
            var pointEvents = events.OfType<IEvent<PointsEarned>>().ToList();
            if (pointEvents.Count == 0) return;

            var accountIds = pointEvents.Select(e => e.Data.AccountId).Distinct().ToArray();
            var owners = await session.Query<AccountOwner>().Where(x => accountIds.Contains(x.Id)).ToListAsync();
            var map = owners.ToDictionary(x => x.Id, x => x.OwnerId);

            foreach (var e in pointEvents)
            {
                if (map.TryGetValue(e.Data.AccountId, out var owner))
                {
                    grouping.AddEvent(owner, e);
                }
            }
        }
    }
}

#endregion

[Collection("OneOffs")]
public class multistream_projection_replay_tests: OneOffConfigurationsContext
{
    private void ConfigureStore()
    {
        StoreOptions(opts =>
        {
            opts.Events.AddEventType<DepositMade>();
            opts.Events.AddEventType<AccountClosed>();
            opts.Events.AddEventType<PointsEarned>();
            opts.Events.AddEventType<CounterIncremented>();

            opts.Projections.Add<AccountBalanceProjection>(ProjectionLifecycle.Async);
            opts.Projections.Add<OwnerPointsProjection>(ProjectionLifecycle.Async);

            // A single-stream aggregation registered alongside to prove the fold collapses
            // single-stream projections to exactly one identity (no regression).
            opts.Projections.LiveStreamAggregation<Counter>();
        });
    }

    private static async Task<List<EventRecord>> AllRecordsAsync(IEventStore explorer, params Guid[] streamIds)
    {
        var list = new List<EventRecord>();
        foreach (var streamId in streamIds)
        {
            await foreach (var e in explorer.ReadStreamAsync(streamId.ToString(), CancellationToken.None))
            {
                list.Add(e);
            }
        }

        return list.OrderBy(x => x.Sequence).ToList();
    }

    [Fact]
    public async Task fans_out_a_cross_stream_event_set_into_one_timeline_per_identity()
    {
        ConfigureStore();

        var acctA = Guid.NewGuid();
        var acctB = Guid.NewGuid();
        var stream1 = Guid.NewGuid();
        var stream2 = Guid.NewGuid();

        // acctA is deposited to across TWO different streams; acctB from one — proving the fan-out
        // keys on AccountId, not on stream.
        theSession.Events.Append(stream1, new DepositMade(acctA, 100), new DepositMade(acctB, 50));
        theSession.Events.Append(stream2, new DepositMade(acctA, 25));
        await theSession.SaveChangesAsync();

        var explorer = (IEventStore)theStore;
        var records = await AllRecordsAsync(explorer, stream1, stream2);

        var result = await explorer.RunMultiStreamProjectionAsync(
            nameof(AccountBalance), records, CancellationToken.None);

        result.ProjectionName.ShouldBe(nameof(AccountBalance));
        result.AggregatesByIdentity.Count.ShouldBe(2);

        var timelineA = result.AggregatesByIdentity[acctA.ToString()];
        timelineA.Steps.Count.ShouldBe(2); // the two DepositMade for acctA, across both streams
        timelineA.FinalState!.Value.GetProperty("Balance").GetInt32().ShouldBe(125);

        var timelineB = result.AggregatesByIdentity[acctB.ToString()];
        timelineB.Steps.Count.ShouldBe(1);
        timelineB.FinalState!.Value.GetProperty("Balance").GetInt32().ShouldBe(50);
    }

    [Fact]
    public async Task honors_should_delete_through_the_real_fold_path()
    {
        ConfigureStore();

        var acct = Guid.NewGuid();
        var stream = Guid.NewGuid();
        theSession.Events.Append(stream, new DepositMade(acct, 100), new AccountClosed(acct));
        await theSession.SaveChangesAsync();

        var explorer = (IEventStore)theStore;
        var records = await AllRecordsAsync(explorer, stream);

        var result = await explorer.RunMultiStreamProjectionAsync(
            nameof(AccountBalance), records, CancellationToken.None);

        var timeline = result.AggregatesByIdentity[acct.ToString()];
        timeline.Steps.Count.ShouldBe(2);

        // Deposit builds the aggregate...
        timeline.Steps[0].After!.Value.GetProperty("Balance").GetInt32().ShouldBe(100);

        // ...AccountClosed deletes it — the After snapshot and the FinalState are both null.
        // A reflection-Apply replay would leave Balance=100 because there is no Apply(AccountClosed).
        timeline.Steps[1].After.ShouldBeNull();
        timeline.FinalState.ShouldBeNull();
    }

    [Fact]
    public async Task runs_enrichment_per_group_reading_present_day_reference_data()
    {
        ConfigureStore();

        var acctA = Guid.NewGuid();
        var acctB = Guid.NewGuid();
        var acctC = Guid.NewGuid();
        var ownerX = Guid.NewGuid();
        var ownerY = Guid.NewGuid();

        // Reference data read by the grouper during enrichment — accounts A and B belong to owner X.
        theSession.Store(new AccountOwner { Id = acctA, OwnerId = ownerX });
        theSession.Store(new AccountOwner { Id = acctB, OwnerId = ownerX });
        theSession.Store(new AccountOwner { Id = acctC, OwnerId = ownerY });
        await theSession.SaveChangesAsync();

        var stream = Guid.NewGuid();
        theSession.Events.Append(stream,
            new PointsEarned(acctA, 10),
            new PointsEarned(acctB, 5),
            new PointsEarned(acctC, 20));
        await theSession.SaveChangesAsync();

        var explorer = (IEventStore)theStore;
        var records = await AllRecordsAsync(explorer, stream);

        var result = await explorer.RunMultiStreamProjectionAsync(
            nameof(OwnerPoints), records, CancellationToken.None);

        // Two owner-keyed aggregates, NOT three account-keyed ones — only possible because the
        // grouper read the AccountOwner docs from the live session (enrichment).
        result.AggregatesByIdentity.Count.ShouldBe(2);
        result.AggregatesByIdentity[ownerX.ToString()].FinalState!.Value.GetProperty("Total").GetInt32().ShouldBe(15);
        result.AggregatesByIdentity[ownerY.ToString()].FinalState!.Value.GetProperty("Total").GetInt32().ShouldBe(20);
    }

    [Fact]
    public async Task single_stream_projection_collapses_to_exactly_one_identity()
    {
        ConfigureStore();

        var streamId = Guid.NewGuid();
        theSession.Events.Append(streamId,
            new CounterIncremented(1),
            new CounterIncremented(2),
            new CounterIncremented(3));
        await theSession.SaveChangesAsync();

        var explorer = (IEventStore)theStore;
        var records = await AllRecordsAsync(explorer, streamId);

        var result = await explorer.RunMultiStreamProjectionAsync(
            nameof(Counter), records, CancellationToken.None);

        result.AggregatesByIdentity.Count.ShouldBe(1);
        var timeline = result.AggregatesByIdentity[streamId.ToString()];
        timeline.Steps.Count.ShouldBe(3);
        timeline.FinalState!.Value.GetProperty("Value").GetInt32().ShouldBe(6);
    }

    [Fact]
    public async Task throws_for_unknown_projection_name()
    {
        ConfigureStore();
        var explorer = (IEventStore)theStore;

        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () =>
        {
            await explorer.RunMultiStreamProjectionAsync(
                "DoesNotExist", Array.Empty<EventRecord>(), CancellationToken.None);
        });
    }
}
