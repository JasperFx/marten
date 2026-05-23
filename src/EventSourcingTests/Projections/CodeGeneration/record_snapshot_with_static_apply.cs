using System;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Projections.CodeGeneration;

// Regression coverage for a user-reported migration scenario: a self-aggregating
// immutable `record` snapshot whose Create AND Apply are BOTH static and take
// IEvent<T>, registered via Snapshot<T>(SnapshotLifecycle.Inline). The new source
// generator emits an IGeneratedSyncEvolver that calls the static methods.
//
// Key detail surfaced during the investigation: a static Apply must take the
// current aggregate as a parameter to *evolve* (accumulate) state. The generator
// passes the current snapshot when the method declares it; a static Apply that
// takes only the event can't see prior state and therefore *replaces* the snapshot
// on every event. Both shapes are exercised below.

public class record_snapshot_with_static_apply : OneOffConfigurationsContext
{
    [Fact]
    public async Task static_apply_with_current_aggregate_accumulates()
    {
        StoreOptions(x => x.Projections.Snapshot<AccountSnapshot>(SnapshotLifecycle.Inline));

        var id = Guid.NewGuid();
        theSession.Events.StartStream<AccountSnapshot>(id,
            new AccountOpened(id, "Alice"),
            new MoneyDeposited(100m),
            new MoneyWithdrawn(30m),
            new MoneyDeposited(10m));
        await theSession.SaveChangesAsync();

        var snapshot = await theSession.LoadAsync<AccountSnapshot>(id);
        snapshot.ShouldNotBeNull();
        snapshot.Id.ShouldBe(id);
        snapshot.Owner.ShouldBe("Alice");
        snapshot.Balance.ShouldBe(80m);
    }

    [Fact]
    public async Task static_apply_without_current_aggregate_replaces_each_event()
    {
        // Documents the gotcha: with no current-aggregate parameter the static Apply
        // cannot carry prior state, so only the latest event's data survives. Add a
        // current-aggregate parameter (as AccountSnapshot above does) to accumulate.
        StoreOptions(x => x.Projections.Snapshot<LatestStatusSnapshot>(SnapshotLifecycle.Inline));

        var id = Guid.NewGuid();
        theSession.Events.StartStream<LatestStatusSnapshot>(id,
            new StatusOpened(id),
            new StatusChanged(id, "in-progress"),
            new StatusChanged(id, "done"));
        await theSession.SaveChangesAsync();

        var snapshot = await theSession.LoadAsync<LatestStatusSnapshot>(id);
        snapshot.ShouldNotBeNull();
        snapshot.Id.ShouldBe(id);
        snapshot.Status.ShouldBe("done");      // latest event wins
        snapshot.ChangeCount.ShouldBe(0);       // not accumulated — no current param to carry it
    }
}

public partial record AccountSnapshot(Guid Id, string Owner, decimal Balance)
{
    public static AccountSnapshot Create(IEvent<AccountOpened> @event)
        => new(@event.Data.AccountId, @event.Data.Owner, 0m);

    public static AccountSnapshot Apply(IEvent<MoneyDeposited> @event, AccountSnapshot current)
        => current with { Balance = current.Balance + @event.Data.Amount };

    public static AccountSnapshot Apply(IEvent<MoneyWithdrawn> @event, AccountSnapshot current)
        => current with { Balance = current.Balance - @event.Data.Amount };
}

public record AccountOpened(Guid AccountId, string Owner);

public record MoneyDeposited(decimal Amount);

public record MoneyWithdrawn(decimal Amount);

public partial record LatestStatusSnapshot(Guid Id, string Status, int ChangeCount)
{
    public static LatestStatusSnapshot Create(IEvent<StatusOpened> @event)
        => new(@event.Data.Id, "open", 0);

    // No current-aggregate parameter: each event rebuilds the snapshot from scratch.
    public static LatestStatusSnapshot Apply(IEvent<StatusChanged> @event)
        => new(@event.Data.Id, @event.Data.Status, 0);
}

public record StatusOpened(Guid Id);

public record StatusChanged(Guid Id, string Status);
