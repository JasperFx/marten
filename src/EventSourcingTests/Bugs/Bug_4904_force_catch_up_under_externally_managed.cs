using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

// Regression guard for https://github.com/JasperFx/marten/issues/4904.
//
// Under DaemonMode.ExternallyManaged an external system (Wolverine's managed event-subscription
// distribution) OWNS the projection agents and their assignment; Marten deliberately registers no
// IProjectionCoordinator of its own (see AddAsyncDaemon — only Solo/HotCold register one).
//
// Before the #4904 mitigation, ForceAllMartenDaemonActivityToCatchUpAsync unconditionally resolved
// IProjectionCoordinator and drove Pause -> Stop -> CatchUp. That was wrong under ExternallyManaged
// in two ways:
//   1. In a plain Marten host (no external coordinator registered) GetRequiredService<IProjectionCoordinator>()
//      threw InvalidOperationException — reproduced directly by this in-repo test (fails on master, passes
//      with the mitigation).
//   2. Where Wolverine DID supply a coordinator, PauseAsync only stopped the running agent instances;
//      Wolverine's supervisor immediately reassigned/restarted them, so the forced CatchUp and the
//      restarted per-tenant agent both advanced the same <proj>:All:<tenant> mt_event_progression row
//      -> ProgressionProgressOutOfOrderException (the actual field failure). That end-to-end race is
//      Wolverine-dependent and belongs in Wolverine's own suite (the active-quiesce fix — the external
//      coordinator suspending agent assignment during a forced catch-up — lives in that coordinator).
//      It cannot be reproduced in Marten's test projects, which carry no Wolverine reference.
//
// The mitigation degrades ForceAll under ExternallyManaged to a read-only wait-for-non-stale that never
// starts, stops, or drives an agent — so it can neither throw on the missing coordinator nor race the
// externally-owned agents into an out-of-order progression write. This test pins that Marten-side contract.
public class Bug_4904_force_catch_up_under_externally_managed
{
    public class LetterCounts
    {
        public Guid Id { get; set; }
        public int ACount { get; set; }
    }

    public record AEvent;

    public partial class LetterCountsProjection: SingleStreamProjection<LetterCounts, Guid>
    {
        public LetterCountsProjection() => Name = "LetterCounts4904";

        public override LetterCounts Evolve(LetterCounts? snapshot, Guid id, IEvent e)
        {
            snapshot ??= new LetterCounts { Id = id };
            if (e.Data is AEvent) snapshot.ACount++;
            return snapshot;
        }
    }

    [Fact(Timeout = 30000)]
    public async Task force_catch_up_under_externally_managed_does_not_resolve_a_coordinator()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(s =>
            {
                s.AddMarten(m =>
                {
                    m.Connection(ConnectionSource.ConnectionString);
                    m.DatabaseSchemaName = "bug4904_externally_managed";
                    m.Projections.Add<LetterCountsProjection>(ProjectionLifecycle.Async);
                }).AddAsyncDaemon(DaemonMode.ExternallyManaged);
            })
            .StartAsync();

        var store = host.Services.GetRequiredService<IDocumentStore>();
        await store.Advanced.Clean.CompletelyRemoveAllAsync();

        // No IProjectionCoordinator is registered under ExternallyManaged. On master ForceAll resolved it
        // via GetRequiredService and threw InvalidOperationException before doing anything else. The
        // mitigation must instead take the read-only ExternallyManaged branch and return cleanly.
        // On master this line threw InvalidOperationException (no IProjectionCoordinator registered);
        // with the mitigation it returns cleanly via the read-only ExternallyManaged branch.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var exceptions = await host.ForceAllMartenDaemonActivityToCatchUpAsync(cts.Token);
        exceptions.ShouldBeEmpty();
    }
}
