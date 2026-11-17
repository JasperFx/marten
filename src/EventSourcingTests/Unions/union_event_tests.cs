#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Unions;

/// <summary>
///     #5735 / jasperfx#1053: a C# union appended as an event is stored as its active case. Marten's
///     <c>EventGraph</c> overrides <c>BuildEvent</c> and <c>AddEventType</c> without reaching the base class,
///     which is where JasperFx unwraps a union, so it does the same itself.
/// </summary>
public class union_event_tests: OneOffConfigurationsContext
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public union_event_tests()
    {
        StoreOptions(opts => opts.UseSystemTextJsonForSerialization());
    }

    private static object[] history() =>
    [
        new UnionAccountOpened("bob"),
        new UnionAccountEvent(new UnionAccountCredited(10m)),
        new UnionAccountEvent(new UnionAccountDebited(3m, "fee")),
        new UnionAccountEvent(new UnionAccountCredited(5m))
    ];

    [Fact]
    public async Task an_appended_union_is_stored_as_its_case()
    {
        var streamId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamId, history());
            await session.SaveChangesAsync(Ct);
        }

        await using var query = theStore.QuerySession();
        var events = await query.Events.FetchStreamAsync(streamId, token: Ct);

        events.Select(x => x.EventType).ShouldBe([
            typeof(UnionAccountOpened), typeof(UnionAccountCredited), typeof(UnionAccountDebited),
            typeof(UnionAccountCredited)
        ]);
        events.Select(x => x.EventTypeName).ShouldBe([
            "union_account_opened", "union_account_credited", "union_account_debited", "union_account_credited"
        ]);
        events[2].Data.ShouldBe(new UnionAccountDebited(3m, "fee"));
    }

    [Fact]
    public async Task appending_to_an_existing_stream_unwraps_too()
    {
        var streamId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamId, new UnionAccountOpened("bob"));
            await session.SaveChangesAsync(Ct);
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(streamId, new UnionAccountEvent(new UnionAccountDebited(1m, "fee")));
            await session.SaveChangesAsync(Ct);
        }

        await using var query = theStore.QuerySession();
        var events = await query.Events.FetchStreamAsync(streamId, token: Ct);
        events.Last().Data.ShouldBe(new UnionAccountDebited(1m, "fee"));
    }

    [Fact]
    public async Task live_aggregation_through_apply_of_the_union()
    {
        var streamId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamId, history());
            await session.SaveChangesAsync(Ct);
        }

        await using var query = theStore.QuerySession();
        var account = await query.Events.AggregateStreamAsync<UnionAccount>(streamId, token: Ct);

        account.ShouldNotBeNull();
        account.Owner.ShouldBe("bob");
        account.Balance.ShouldBe(12m);
        account.Debits.ShouldBe(1);
    }

    [Fact]
    public async Task an_inline_snapshot_through_apply_of_the_union()
    {
        StoreOptions(opts =>
        {
            opts.UseSystemTextJsonForSerialization();
            opts.Projections.Snapshot<UnionAccount>(SnapshotLifecycle.Inline);
        });

        var streamId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamId, history());
            await session.SaveChangesAsync(Ct);
        }

        await using var query = theStore.QuerySession();
        var account = await query.LoadAsync<UnionAccount>(streamId, Ct);

        account.ShouldNotBeNull();
        account.Balance.ShouldBe(12m);
        account.Debits.ShouldBe(1);
    }

    [Fact]
    public void registering_a_union_registers_its_cases()
    {
        StoreOptions(opts =>
        {
            opts.UseSystemTextJsonForSerialization();
            opts.Events.AddEventType<UnionAccountEvent>();
        });

        var known = theStore.Options.EventGraph.AllKnownEventTypes().Select(x => x.EventType).ToArray();
        known.ShouldContain(typeof(UnionAccountCredited));
        known.ShouldContain(typeof(UnionAccountDebited));
        known.ShouldNotContain(typeof(UnionAccountEvent));
    }

    [Fact]
    public async Task a_default_union_has_no_event_to_append()
    {
        await using var session = theStore.LightweightSession();

        Should.Throw<InvalidOperationException>(() =>
                session.Events.StartStream(Guid.NewGuid(), new UnionAccountEvent()))
            .Message.ShouldContain("no active case");
    }
}
