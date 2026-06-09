#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Storage;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace TenantPartitionedEventsTests.Regressions;

/// <summary>
/// Regression reproduction for
/// <see href="https://github.com/JasperFx/marten/issues/4679">#4679</see> —
/// follow-up to the #4665 fix. The new per-tenant catch-up path
/// (<c>JasperFxAsyncDaemon.catchUpPerTenantAsync</c>, introduced in
/// JasperFx.Events 2.8.2) throws
/// <c>23505: duplicate key value violates unique constraint "pk_mt_event_progression"</c>
/// for store-global (<c>:All</c>) projection shards when
/// <c>Events.UseTenantPartitionedEvents</c> is on and more than one tenant
/// participates in the catch-up. The conflicting key is the store-global
/// identity (no tenant suffix), so something in the per-tenant iteration loop
/// writes the store-global progression-row name once per tenant instead of
/// per-tenant scoped names.
///
/// <para>
/// Stack from the original report:
/// <code>
/// 23505: duplicate key value violates unique constraint "pk_mt_event_progression"
/// DETAIL: Key (name)=(PreGoLiveMidwiferyProvidedCare:V9:All) already exists.
///   at JasperFxAsyncDaemon`3.catchUpPerTenantAsync ... JasperFxAsyncDaemon.cs:989
///   at JasperFxAsyncDaemon`3.CatchUpAsync ... JasperFxAsyncDaemon.cs:921
///   at TestingExtensions.ForceAllMartenDaemonActivityToCatchUpAsync ...
/// </code>
/// </para>
///
/// <para>
/// <b>Repro shape</b>: same partitioning + Conjoined config as Bug_4665, add
/// 2+ tenants, append events to each, call <c>BuildProjectionDaemonAsync().CatchUpAsync</c>
/// (same entry point ForceAllMartenDaemonActivityToCatchUpAsync drives). With
/// store-global async projections registered, the second tenant's per-tenant
/// catchup hits 23505 trying to insert the store-global progression row that
/// the first tenant's catchup already created.
/// </para>
///
/// <para>
/// <b>Repro status</b>: this Marten-side single-DB Conjoined repro does NOT
/// trigger the 23505 in isolation -- multiple attempts (host-based via
/// <c>ForceAllMartenDaemonActivityToCatchUpAsync</c> with the live daemon
/// started, store-direct via <c>BuildProjectionDaemonAsync().CatchUpAsync</c>)
/// all pass with three tenants × one event each. The user's reported config
/// includes <c>MultiTenantedWithShardedDatabases</c> + a much wider set of
/// async projections (<c>ClientClaimLine:V6:All</c>, <c>Invoice:V7:All</c>,
/// etc -- ~11 enumerated in the report) and per-tenant sequence positions
/// likely well above zero from accumulated production traffic. One of those
/// dimensions appears load-bearing for the bug.
/// </para>
///
/// <para>
/// <b>Fix is JasperFx-side</b>: <c>JasperFxAsyncDaemon.catchUpPerTenantAsync</c>
/// at <c>src/JasperFx.Events/Daemon/JasperFxAsyncDaemon.cs:945-991</c>.
/// The Marten reproduction <see cref="Skip"/>'s itself; bumping
/// <c>JasperFx.Events</c> past the fix version + replacing the <c>Skip</c>
/// with an <c>[Fact]</c> attribute is the consume step (in addition to,
/// ideally, a tighter Marten-side repro than this fixture once the bug's
/// actual trigger is pinned).
/// </para>
/// </summary>
public partial class Bug_4679_catch_up_per_tenant_23505
{
    private readonly ITestOutputHelper _output;

    public Bug_4679_catch_up_per_tenant_23505(ITestOutputHelper output)
    {
        _output = output;
    }

    private static readonly string SchemaName = $"bug4679_p{Environment.ProcessId}";

    public class TripDistance
    {
        public Guid Id { get; set; }
        public double Distance { get; set; }
        public int Version { get; set; }
    }

    public record TripStarted(Guid Id);
    public record TripLeg(double Distance);

    public partial class TripDistanceProjection: SingleStreamProjection<TripDistance, Guid>
    {
        public TripDistanceProjection()
        {
            // Store-global :All shard -- the exact shape the bug fires on (the
            // user's report enumerates ClientClaimLine:V6:All, Invoice:V7:All,
            // etc -- every async projection registered without explicit
            // tenancy sharding gets a :All shard).
            Name = "Bug4679TripDistance";
        }

        public void Apply(TripDistance agg, TripLeg @event) => agg.Distance += @event.Distance;
    }

    // #4679 fixed in JasperFx.Events 2.9.0 (jasperfx#419 — composite member stages now compose
    // ShardName with the parent's tenant id instead of a bare store-global name). Unskipped: this
    // single-DB SingleStream repro never triggered the 23505 on its own (the real trigger was
    // composite projections, covered by Sharded/Bug_4679_sharded_catch_up_23505), but it stays as
    // a per-tenant catch-up advance + no-23505 guard.
    [Fact]
    public async Task force_catch_up_across_multiple_tenants_does_not_throw_23505()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = SchemaName;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Policies.AllDocumentsAreMultiTenanted();

            opts.Projections.Add<TripDistanceProjection>(ProjectionLifecycle.Async);
        });
        await store.Advanced.Clean.CompletelyRemoveAllAsync();
        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        // Two tenants minimum — the bug needs ≥2 to fire (first tenant's loop
        // iteration inserts the store-global progression row; second tenant's
        // iteration collides).
        var tenants = Enumerable.Range(0, 3)
            .Select(_ => $"t_{Guid.NewGuid():N}".Substring(0, 12))
            .ToArray();
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenants);

        // Seed each tenant with one stream + one TripLeg so the per-tenant
        // catch-up has something to advance through (an empty tenant's
        // `ceiling == 0` short-circuit at JasperFxAsyncDaemon.cs:977 would
        // skip the iteration and mask the bug).
        var lastTripPerTenant = new Dictionary<string, Guid>();
        foreach (var tenant in tenants)
        {
            var streamId = Guid.NewGuid();
            lastTripPerTenant[tenant] = streamId;
            await using var session = store.LightweightSession(tenant);
            session.Events.StartStream<TripDistance>(streamId,
                new TripStarted(streamId),
                new TripLeg(1.0));
            await session.SaveChangesAsync();
        }

        // Drive the buggy path via ForceAllMartenDaemonActivityToCatchUpAsync — matches
        // the user's stack trace exactly. The internal flow is identical to
        // daemon.CatchUpAsync() but goes through IProjectionCoordinator + the live
        // daemon AddAsyncDaemon(Solo) started, which is the configuration the user reported.
        Exception? thrown = null;
        var hostBuilder = Host.CreateApplicationBuilder();
        hostBuilder.Services.AddMarten(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = SchemaName;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Projections.Add<TripDistanceProjection>(ProjectionLifecycle.Async);
        }).AddAsyncDaemon(DaemonMode.Solo);

        using var host = hostBuilder.Build();
        await host.StartAsync();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            // Exact entry point in the user's stack trace.
            var exceptions = await host.ForceAllMartenDaemonActivityToCatchUpAsync(cts.Token);
            if (exceptions.Count > 0)
            {
                thrown = new AggregateException(exceptions);
            }
        }
        catch (Exception e)
        {
            thrown = e;
        }
        await host.StopAsync();

        // Headline: no 23505 anywhere in the exception chain. Drill into
        // AggregateException so the per-shard exceptions surface.
        if (thrown != null)
        {
            var allMessages = string.Join("\n", Flatten(thrown).Select(x => $"  {x.GetType().Name}: {x.Message}"));
            _output.WriteLine("CatchUpAsync threw:\n" + allMessages);
        }

        Flatten(thrown).ShouldNotContain(
            x => x.Message.Contains("23505", StringComparison.Ordinal)
                 || x.Message.Contains("pk_mt_event_progression", StringComparison.Ordinal),
            "CatchUpAsync under per-tenant partitioning must not throw 23505 on the global :All " +
            "shard's progression row -- the per-tenant catch-up loop should write tenant-scoped " +
            "progression rows, not the store-global identity.");

        // Sanity: each tenant's projection actually advanced (the fix mustn't
        // regress the original #4665 advance-correctness pin).
        foreach (var (tenant, streamId) in lastTripPerTenant)
        {
            await using var query = store.QuerySession(tenant);
            var doc = await query.LoadAsync<TripDistance>(streamId);
            doc.ShouldNotBeNull($"tenant {tenant} stream {streamId} should have a projected doc after catch-up");
            doc.Distance.ShouldBe(1.0);
        }
    }

    private static IEnumerable<Exception> Flatten(Exception? root)
    {
        if (root is null) yield break;
        if (root is AggregateException agg)
        {
            foreach (var inner in agg.Flatten().InnerExceptions)
            {
                yield return inner;
                if (inner.InnerException != null) foreach (var x in Flatten(inner.InnerException)) yield return x;
            }
        }
        else
        {
            yield return root;
            if (root.InnerException != null) foreach (var x in Flatten(root.InnerException)) yield return x;
        }
    }
}
