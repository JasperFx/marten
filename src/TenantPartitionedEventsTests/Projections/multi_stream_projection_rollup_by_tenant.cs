using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Projections;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.Projections;

/// <summary>
/// #4617 section 3c — <see cref="MultiStreamProjection{TDoc,TId}"/> with
/// <c>RollUpByTenant()</c> under <c>UseTenantPartitionedEvents</c>. The
/// rollup slicer groups every event by <see cref="IEvent.TenantId"/>, then
/// writes one document per tenant id keyed by that tenant id (string-typed
/// identity). Pins (a) one rollup doc per tenant after a full async rebuild,
/// (b) each tenant's rollup totals only sum that tenant's events (no
/// cross-tenant arithmetic leak).
///
/// <para>
/// Own-store: registering <see cref="TenantRollupProjection"/> on the shared
/// fixture would change the projection set for every sibling test, and the
/// rollup projection materializes into the default tenant slot (since the
/// slice group's TenantId is <c>DefaultTenantId</c> — see
/// <c>TenantRollupSlicer.SliceAsync</c>), which a shared store could collide
/// on across tests.
/// </para>
///
/// <para>
/// The rollup pattern + <c>AllDocumentsAreMultiTenanted</c> writes the rollup
/// document to the default tenant slot (the slicer hardcodes
/// <c>StorageConstants.DefaultTenantId</c> as the group's <c>TenantId</c>,
/// and our policy makes the doc table tenant-aware). So reads come from a
/// default-tenant query session — NOT a per-tenant one. The doc identity
/// itself is the tenant id string, which is how each tenant's rollup is
/// looked up.
/// </para>
/// </summary>
public class multi_stream_projection_rollup_by_tenant : IAsyncLifetime
{
    private string _schema = null!;
    private DocumentStore _store = null!;

    public async Task InitializeAsync()
    {
        _schema = $"tp_rollup_{Environment.ProcessId}_{Guid.NewGuid():N}".Substring(0, 32);

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        try { await conn.DropSchemaAsync(_schema); } catch { }

        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = _schema;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Policies.AllDocumentsAreMultiTenanted();

            opts.Events.AddEventType<AccountOpened>();
            opts.Events.AddEventType<TransactionPosted>();

            // Short alias keeps the partition / index identifiers under PG's
            // 64-byte limit when combined with tenant slot names downstream.
            opts.Schema.For<TenantRollup>().DocumentAlias("p2c_tenant_rollup");

            opts.Projections.Add<TenantRollupProjection>(ProjectionLifecycle.Async);
        });

        await _store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));
    }

    public Task DisposeAsync()
    {
        _store?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task RollUpByTenant_produces_one_doc_per_tenant_aggregating_their_events()
    {
        // Two tenants drive their own AccountOpened + TransactionPosted streams.
        // After the daemon catches up, the rollup projection materializes ONE
        // doc per tenant whose Id == tenant id and whose totals reflect ONLY
        // that tenant's events.
        var alpha = "alpha_" + Guid.NewGuid().ToString("N")[..8];
        var beta = "beta_" + Guid.NewGuid().ToString("N")[..8];
        await _store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);

        await using (var session = _store.LightweightSession(alpha))
        {
            var acct = Guid.NewGuid();
            session.Events.StartStream(acct,
                new AccountOpened(acct),
                new TransactionPosted(acct, 100m),
                new TransactionPosted(acct, 50m));
            await session.SaveChangesAsync();
        }
        await using (var session = _store.LightweightSession(beta))
        {
            var acct = Guid.NewGuid();
            session.Events.StartStream(acct,
                new AccountOpened(acct),
                new TransactionPosted(acct, 7m),
                new TransactionPosted(acct, 3m),
                new TransactionPosted(acct, 10m));
            await session.SaveChangesAsync();
        }

        // Per-tenant rebuild explicitly walks each tenant's events from
        // scratch — preferred over StartAllAsync + WaitForNonStaleData here
        // since the rollup grouping turns the per-tenant data into single docs
        // by tenant id and we want determinism on the assertion, not race-y
        // wait-for-completion semantics.
        using (var daemon = await _store.BuildProjectionDaemonAsync())
        {
            await daemon.RebuildProjectionAsync(TenantRollupProjection.ProjectionName, alpha, CancellationToken.None);
            await daemon.RebuildProjectionAsync(TenantRollupProjection.ProjectionName, beta, CancellationToken.None);
        }

        // The rollup doc lives in the default tenant slot (slicer hardcodes
        // DefaultTenantId as the group's TenantId) — read from a default-tenant
        // session keyed by the doc identity (= tenant id string).
        await using var query = _store.QuerySession();
        var alphaRollup = await query.LoadAsync<TenantRollup>(alpha);
        var betaRollup = await query.LoadAsync<TenantRollup>(beta);

        alphaRollup.ShouldNotBeNull("alpha must have a rollup doc keyed by its tenant id");
        alphaRollup!.Total.ShouldBe(150m, "alpha's 100 + 50 = 150 — no beta arithmetic leak");
        alphaRollup.TransactionCount.ShouldBe(2);

        betaRollup.ShouldNotBeNull("beta must have a rollup doc keyed by its tenant id");
        betaRollup!.Total.ShouldBe(20m, "beta's 7 + 3 + 10 = 20 — no alpha arithmetic leak");
        betaRollup.TransactionCount.ShouldBe(3);
    }

    [Fact]
    public async Task RollUpByTenant_doc_for_tenant_A_only_aggregates_As_events()
    {
        // Direct sibling-isolation pin: append a sequence of identical
        // TransactionPosted amounts to TWO tenants and confirm each tenant's
        // rollup doc totals are exactly that tenant's events, not the union.
        var alpha = "alpha_" + Guid.NewGuid().ToString("N")[..8];
        var beta = "beta_" + Guid.NewGuid().ToString("N")[..8];
        await _store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);

        var alphaAcct = Guid.NewGuid();
        await using (var session = _store.LightweightSession(alpha))
        {
            session.Events.StartStream(alphaAcct, new AccountOpened(alphaAcct));
            session.Events.Append(alphaAcct,
                new TransactionPosted(alphaAcct, 1m),
                new TransactionPosted(alphaAcct, 1m),
                new TransactionPosted(alphaAcct, 1m));
            await session.SaveChangesAsync();
        }

        var betaAcct = Guid.NewGuid();
        await using (var session = _store.LightweightSession(beta))
        {
            session.Events.StartStream(betaAcct, new AccountOpened(betaAcct));
            session.Events.Append(betaAcct,
                new TransactionPosted(betaAcct, 1m),
                new TransactionPosted(betaAcct, 1m));
            await session.SaveChangesAsync();
        }

        using (var daemon = await _store.BuildProjectionDaemonAsync())
        {
            await daemon.RebuildProjectionAsync(TenantRollupProjection.ProjectionName, alpha, CancellationToken.None);
            await daemon.RebuildProjectionAsync(TenantRollupProjection.ProjectionName, beta, CancellationToken.None);
        }

        await using var query = _store.QuerySession();
        var alphaRollup = await query.LoadAsync<TenantRollup>(alpha);
        var betaRollup = await query.LoadAsync<TenantRollup>(beta);

        alphaRollup.ShouldNotBeNull();
        alphaRollup!.TransactionCount.ShouldBe(3,
            "alpha appended 3 transactions — beta's 2 cannot bleed into the rollup");

        betaRollup.ShouldNotBeNull();
        betaRollup!.TransactionCount.ShouldBe(2,
            "beta appended 2 transactions — alpha's 3 cannot bleed into the rollup");
    }
}

public record AccountOpened(Guid AccountId);
public record TransactionPosted(Guid AccountId, decimal Amount);

public class TenantRollup
{
    // = tenant id (string-keyed since the rollup slicer requires string Id).
    [Identity]
    public string Id { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public int TransactionCount { get; set; }
}

public partial class TenantRollupProjection : MultiStreamProjection<TenantRollup, string>
{
    public const string ProjectionName = "TenantRollup";

    public TenantRollupProjection()
    {
        Name = ProjectionName;
        // Opts into TenancyGrouping.RollUpByTenant — every event slices into
        // a SliceGroup keyed by IEvent.TenantId, materializing one TenantRollup
        // per tenant id (Id type must be string or a strong-typed-id wrapping
        // a string, enforced in RollUpByTenant()).
        RollUpByTenant();
    }

    public void Apply(TenantRollup r, TransactionPosted e)
    {
        r.Total += e.Amount;
        r.TransactionCount++;
    }
}
