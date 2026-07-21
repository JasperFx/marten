#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Grouping;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using System.Collections.Generic;

namespace EventSourcingTests.Explorer;

#region aggregate-to-many sample types

public record MoneyDeposited(Guid AccountId, int Amount);
public record AccountFrozen(Guid AccountId);

public class Balance
{
    public Guid Id { get; set; }
    public int Amount { get; set; }
}

public partial class BalanceProjection: MultiStreamProjection<Balance, Guid>
{
    public BalanceProjection()
    {
        Identity<MoneyDeposited>(e => e.AccountId);
        Identity<AccountFrozen>(e => e.AccountId);
    }

    public void Apply(MoneyDeposited e, Balance b) => b.Amount += e.Amount;

    public bool ShouldDelete(AccountFrozen e) => true;
}

public record LoyaltyEarned(Guid CardId, int Points);

public class CardOwner
{
    public Guid Id { get; set; } // card id
    public Guid MemberId { get; set; }
}

public class MemberLoyalty
{
    public Guid Id { get; set; }
    public int Points { get; set; }
}

public partial class MemberLoyaltyProjection: MultiStreamProjection<MemberLoyalty, Guid>
{
    public MemberLoyaltyProjection()
    {
        CustomGrouping(new Grouper());
    }

    public void Apply(LoyaltyEarned e, MemberLoyalty agg) => agg.Points += e.Points;

    public class Grouper: IAggregateGrouper<Guid>
    {
        public async Task Group(IQuerySession session, IReadOnlyList<IEvent> events, IEventGrouping<Guid> grouping)
        {
            var earned = events.OfType<IEvent<LoyaltyEarned>>().ToList();
            if (earned.Count == 0) return;

            var cardIds = earned.Select(e => e.Data.CardId).Distinct().ToArray();
            var owners = await session.Query<CardOwner>().Where(x => cardIds.Contains(x.Id)).ToListAsync();
            var map = owners.ToDictionary(x => x.Id, x => x.MemberId);

            foreach (var e in earned)
            {
                if (map.TryGetValue(e.Data.CardId, out var member))
                {
                    grouping.AddEvent(member, e);
                }
            }
        }
    }
}

#endregion

[Collection("OneOffs")]
public class aggregate_to_many_tests: OneOffConfigurationsContext
{
    private void ConfigureStore()
    {
        StoreOptions(opts =>
        {
            opts.Events.AddEventType<MoneyDeposited>();
            opts.Events.AddEventType<AccountFrozen>();
            opts.Events.AddEventType<LoyaltyEarned>();

            opts.Projections.Add<BalanceProjection>(ProjectionLifecycle.Async);
            opts.Projections.Add<MemberLoyaltyProjection>(ProjectionLifecycle.Async);
        });
    }

    [Fact]
    public async Task fans_a_cross_stream_query_out_to_one_aggregate_per_identity()
    {
        ConfigureStore();

        var acctA = Guid.NewGuid();
        var acctB = Guid.NewGuid();
        var stream1 = Guid.NewGuid();
        var stream2 = Guid.NewGuid();

        // acctA deposited across two streams; acctB in one — the fan-out keys on AccountId, not stream.
        theSession.Events.Append(stream1, new MoneyDeposited(acctA, 100), new MoneyDeposited(acctB, 50));
        theSession.Events.Append(stream2, new MoneyDeposited(acctA, 25));
        await theSession.SaveChangesAsync();

        var aggregates = await theSession.Events.QueryAllRawEvents()
            .Where(e => e.StreamId == stream1 || e.StreamId == stream2)
            .AggregateToManyAsync<Balance>();

        aggregates.Count.ShouldBe(2);

        // Identity is stamped on each aggregate...
        aggregates.Single(x => x.Id == acctA).Amount.ShouldBe(125);
        aggregates.Single(x => x.Id == acctB).Amount.ShouldBe(50);
    }

    [Fact]
    public async Task excludes_aggregates_that_should_delete()
    {
        ConfigureStore();

        var acctA = Guid.NewGuid();
        var acctB = Guid.NewGuid();
        var stream = Guid.NewGuid();

        theSession.Events.Append(stream,
            new MoneyDeposited(acctA, 100),
            new MoneyDeposited(acctB, 200),
            new AccountFrozen(acctB));
        await theSession.SaveChangesAsync();

        var aggregates = await theSession.Events.QueryAllRawEvents()
            .Where(e => e.StreamId == stream)
            .AggregateToManyAsync<Balance>();

        // acctB is frozen (ShouldDelete) and so is absent; only acctA survives.
        aggregates.Count.ShouldBe(1);
        aggregates.Single().Id.ShouldBe(acctA);
        aggregates.Single().Amount.ShouldBe(100);
    }

    [Fact]
    public async Task enrichment_reads_reference_data_from_the_live_session()
    {
        ConfigureStore();

        var cardA = Guid.NewGuid();
        var cardB = Guid.NewGuid();
        var cardC = Guid.NewGuid();
        var memberX = Guid.NewGuid();
        var memberY = Guid.NewGuid();

        // Present-day reference data the grouper reads during enrichment.
        theSession.Store(new CardOwner { Id = cardA, MemberId = memberX });
        theSession.Store(new CardOwner { Id = cardB, MemberId = memberX });
        theSession.Store(new CardOwner { Id = cardC, MemberId = memberY });
        await theSession.SaveChangesAsync();

        var stream = Guid.NewGuid();
        theSession.Events.Append(stream,
            new LoyaltyEarned(cardA, 10),
            new LoyaltyEarned(cardB, 5),
            new LoyaltyEarned(cardC, 20));
        await theSession.SaveChangesAsync();

        var aggregates = await theSession.Events.QueryAllRawEvents()
            .Where(e => e.StreamId == stream)
            .AggregateToManyAsync<MemberLoyalty>();

        // Member-keyed, not card-keyed — only possible because the grouper read CardOwner from the session.
        aggregates.Count.ShouldBe(2);
        aggregates.Single(x => x.Id == memberX).Points.ShouldBe(15);
        aggregates.Single(x => x.Id == memberY).Points.ShouldBe(20);
    }

    [Fact]
    public async Task empty_query_returns_empty_list()
    {
        ConfigureStore();

        var aggregates = await theSession.Events.QueryAllRawEvents()
            .Where(e => e.StreamId == Guid.NewGuid()) // matches nothing
            .AggregateToManyAsync<Balance>();

        aggregates.ShouldBeEmpty();
    }

    [Fact]
    public async Task throws_when_no_projection_produces_the_aggregate_type()
    {
        ConfigureStore();

        await Should.ThrowAsync<ArgumentException>(async () =>
        {
            await theSession.Events.QueryAllRawEvents().AggregateToManyAsync<UnrelatedAggregate>();
        });
    }

    public class UnrelatedAggregate
    {
        public Guid Id { get; set; }
    }
}
