using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.Projections;

/// <summary>
/// #4617 section 3c (#4651) — pin a user-supplied <see cref="SingleStreamProjection{TDoc,TId}.DetermineActionAsync"/>
/// override under <c>UseTenantPartitionedEvents</c>:
///
/// <list type="bullet">
///   <item>The <see cref="IQuerySession"/> argument has a tenant-scoped
///     <c>TenantId</c> matching the writing session's tenant on every
///     invocation. Cross-tenant fan-in is an async-daemon-only concern; for
///     inline projections every call sees exactly one tenant id, never the
///     <c>*DEFAULT*</c> sentinel.</item>
///   <item>Returning <see cref="ActionType.Delete"/> removes ONLY the slicing
///     tenant's projected doc — the sibling tenant's doc with the same stream
///     id stays intact (per-tenant doc isolation via the multi-tenant policy
///     holds across the delete path).</item>
/// </list>
///
/// <para>
/// Own-store because <see cref="DetermineCounter"/>'s projection registration
/// is store-wide; the shared fixture intentionally registers a different
/// projection set.
/// </para>
/// </summary>
public class determine_action_async_per_tenant : PartitionedStoreContext
{
    protected override string SchemaPrefix => "tp_da";

    protected override void ConfigureStore(StoreOptions opts)
    {
        opts.Events.AddEventType<DetermineIncrementEvent>();
        opts.Events.AddEventType<DetermineResetEvent>();

        // Inline so the projection runs in the writing session.
        opts.Projections.Add<DetermineCounterProjection>(ProjectionLifecycle.Inline);
    }

    public override async ValueTask InitializeAsync()
    {
        // Reset cross-test capture so each test starts clean.
        DetermineCounterProjection.ObservedTenants.Clear();

        await base.InitializeAsync();
    }

    [Fact]
    public async Task determine_action_async_observes_writing_tenant_id_per_slice()
    {
        // Both tenants append events that drive the projection. The override
        // captures session.TenantId on every call. Pin: the set of observed
        // tenant ids matches the writing tenants exactly — no *DEFAULT*, no
        // cross-tenant leak.
        await Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta");

        await using (var session = Store.LightweightSession("alpha"))
        {
            session.Events.StartStream(Guid.NewGuid(),
                new DetermineIncrementEvent(), new DetermineIncrementEvent());
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        await using (var session = Store.LightweightSession("beta"))
        {
            session.Events.StartStream(Guid.NewGuid(),
                new DetermineIncrementEvent());
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Every captured tenant id is one of the writing tenants — never
        // *DEFAULT*, never another tenant's slot.
        DetermineCounterProjection.ObservedTenants.Count.ShouldBeGreaterThan(0,
            "DetermineActionAsync should have been called at least once");

        foreach (var tid in DetermineCounterProjection.ObservedTenants)
        {
            (tid == "alpha" || tid == "beta").ShouldBeTrue(
                "observed an unexpected tenant id in DetermineActionAsync: " + tid);
        }

        // Both writing tenants appear in the capture set.
        DetermineCounterProjection.ObservedTenants.ShouldContain("alpha");
        DetermineCounterProjection.ObservedTenants.ShouldContain("beta");
    }

    [Fact]
    public async Task delete_via_determine_action_scoped_to_writing_tenant_only()
    {
        // Two tenants drive the SAME stream id (legal under partitioning —
        // each tenant's slot has its own stream). alpha sends one
        // DetermineResetEvent which returns ActionType.Delete. beta's doc
        // for the same stream id must NOT be deleted.
        await Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta");

        var sharedStream = Guid.NewGuid();

        // alpha: start + increment once → doc with Count = 1.
        await using (var session = Store.LightweightSession("alpha"))
        {
            session.Events.StartStream(sharedStream, new DetermineIncrementEvent());
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        // beta: start + increment twice → doc with Count = 2.
        await using (var session = Store.LightweightSession("beta"))
        {
            session.Events.StartStream(sharedStream,
                new DetermineIncrementEvent(), new DetermineIncrementEvent());
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Confirm both docs exist with their counts.
        await using (var alphaQuery = Store.QuerySession("alpha"))
        {
            (await alphaQuery.LoadAsync<DetermineCounter>(sharedStream, TestContext.Current.CancellationToken))!.Count.ShouldBe(1);
        }
        await using (var betaQuery = Store.QuerySession("beta"))
        {
            (await betaQuery.LoadAsync<DetermineCounter>(sharedStream, TestContext.Current.CancellationToken))!.Count.ShouldBe(2);
        }

        // alpha sends a DetermineResetEvent → projection returns Delete →
        // alpha's doc is removed.
        await using (var session = Store.LightweightSession("alpha"))
        {
            session.Events.Append(sharedStream, new DetermineResetEvent());
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Pin: alpha's doc is gone, beta's is untouched (Count still 2).
        await using (var alphaQuery = Store.QuerySession("alpha"))
        {
            (await alphaQuery.LoadAsync<DetermineCounter>(sharedStream, TestContext.Current.CancellationToken))
                .ShouldBeNull("alpha's reset → Delete should have removed alpha's doc");
        }
        await using (var betaQuery = Store.QuerySession("beta"))
        {
            var betaDoc = await betaQuery.LoadAsync<DetermineCounter>(sharedStream, TestContext.Current.CancellationToken);
            betaDoc.ShouldNotBeNull("beta's doc must NOT be deleted by alpha's reset (tenant isolation)");
            betaDoc!.Count.ShouldBe(2, "beta's count must be unchanged");
        }
    }
}

public record DetermineIncrementEvent;
public record DetermineResetEvent;

public class DetermineCounter
{
    public Guid Id { get; set; }
    public int Count { get; set; }
}

public partial class DetermineCounterProjection : SingleStreamProjection<DetermineCounter, Guid>
{
    public static readonly ConcurrentBag<string> ObservedTenants = new();

    public DetermineCounterProjection() { Name = "DetermineCounter"; }

    public override ValueTask<(DetermineCounter, ActionType)> DetermineActionAsync(
        IQuerySession session,
        DetermineCounter snapshot,
        Guid identity,
        IIdentitySetter<DetermineCounter, Guid> identitySetter,
        IReadOnlyList<IEvent> events,
        CancellationToken cancellation)
    {
        // Critical pin: session.TenantId is the writing tenant — captured for
        // assertion.
        ObservedTenants.Add(session.TenantId);

        snapshot ??= new DetermineCounter { Id = identity };

        foreach (var e in events)
        {
            switch (e.Data)
            {
                case DetermineIncrementEvent:
                    snapshot.Count++;
                    break;
                case DetermineResetEvent:
                    // Returning Delete short-circuits the write — the doc is
                    // removed from THIS tenant's slot. Sibling tenants are
                    // unaffected because the projection runs per-slice and
                    // each slice carries one tenant.
                    return new ValueTask<(DetermineCounter, ActionType)>((snapshot, ActionType.Delete));
            }
        }

        return new ValueTask<(DetermineCounter, ActionType)>((snapshot, ActionType.Store));
    }
}
