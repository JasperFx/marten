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
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.Projections;

/// <summary>
/// #4617 section 3c — <see cref="EventProjection"/> behavior under
/// <c>UseTenantPartitionedEvents</c>. Pins (a) inline materialization stays
/// per-tenant (each tenant's projected docs land only in its own session view),
/// and (b) per-tenant async <c>RebuildProjectionAsync(name, tenantId)</c> only
/// materializes docs for the rebuilding tenant, leaving sibling tenants empty
/// until they are rebuilt themselves.
///
/// <para>
/// Own-store (not the shared fixture) because <see cref="LegLoggerProjection"/>
/// is local to this file and registering it on the shared store would pollute
/// every sibling test on the fixture. Local event + document types keep this
/// test self-contained — the fixture's Trip* types are not reused here so a
/// future fixture refactor cannot drift the assertions.
/// </para>
/// </summary>
public class event_projection_per_tenant : IAsyncLifetime
{
    private string _schema = null!;
    private DocumentStore _store = null!;

    public async Task InitializeAsync()
    {
        _schema = $"tp_evproj_{Environment.ProcessId}_{Guid.NewGuid():N}".Substring(0, 32);

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

            opts.Events.AddEventType<LegEvent>();
            opts.Schema.For<LegLog>().Identity(x => x.Id).DocumentAlias("p2c_leg_log");

            opts.Projections.Add<LegLoggerProjection>(ProjectionLifecycle.Inline);
        });

        await _store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));
    }

    public Task DisposeAsync()
    {
        _store?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task event_projection_inline_materializes_only_in_owning_tenants_session_view()
    {
        // Inline EventProjection — Create(IEvent<LegEvent>) emits one LegLog
        // per event. Pin: alpha's 3 legs materialize 3 LegLog docs visible only
        // in alpha's session; beta (no events appended) sees no docs.
        var alpha = "alpha_" + Guid.NewGuid().ToString("N")[..8];
        var beta = "beta_" + Guid.NewGuid().ToString("N")[..8];
        await _store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);

        var alphaStream = Guid.NewGuid();
        await using (var session = _store.LightweightSession(alpha))
        {
            session.Events.StartStream(alphaStream,
                new LegEvent(1.0), new LegEvent(2.0), new LegEvent(3.0));
            await session.SaveChangesAsync();
        }

        await using var qa = _store.QuerySession(alpha);
        var alphaLogs = await qa.Query<LegLog>().ToListAsync();
        alphaLogs.Count.ShouldBe(3,
            "EventProjection.Create emits one LegLog per LegEvent — alpha appended 3, so alpha's session sees 3");
        alphaLogs.Select(l => l.Distance).OrderBy(d => d).ShouldBe(new[] { 1.0, 2.0, 3.0 });

        await using var qb = _store.QuerySession(beta);
        var betaLogs = await qb.Query<LegLog>().ToListAsync();
        betaLogs.ShouldBeEmpty(
            "beta appended no events — its tenant slot must be empty (no cross-tenant doc leak)");
    }

    [Fact]
    public async Task event_projection_async_via_RebuildProjectionAsync_materializes_per_tenant()
    {
        // Rebuild from scratch for ONE tenant only. The Inline projection
        // already materialized docs on append for both tenants — so the test
        // first wipes alpha's docs via DeleteProjectionProgressAsync(name,
        // tenantId, …) and then rebuilds JUST alpha. Beta's docs must remain
        // byte-identical (never touched by the per-tenant rebuild).
        var alpha = "alpha_" + Guid.NewGuid().ToString("N")[..8];
        var beta = "beta_" + Guid.NewGuid().ToString("N")[..8];
        await _store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);

        var alphaStream = Guid.NewGuid();
        var betaStream = Guid.NewGuid();

        await using (var session = _store.LightweightSession(alpha))
        {
            session.Events.StartStream(alphaStream, new LegEvent(10), new LegEvent(20));
            await session.SaveChangesAsync();
        }
        await using (var session = _store.LightweightSession(beta))
        {
            session.Events.StartStream(betaStream, new LegEvent(7), new LegEvent(13));
            await session.SaveChangesAsync();
        }

        // Pre-state: inline already materialized both tenants.
        await using (var qa = _store.QuerySession(alpha))
        {
            (await qa.Query<LegLog>().ToListAsync()).Count.ShouldBe(2);
        }
        await using (var qb = _store.QuerySession(beta))
        {
            (await qb.Query<LegLog>().ToListAsync()).Count.ShouldBe(2);
        }

        // Wipe alpha's docs + progression — Phase 2c teardown. Beta untouched.
        var es = (IEventStore<IDocumentOperations, IQuerySession>)_store;
        await es.DeleteProjectionProgressAsync(
            (IEventDatabase)_store.Storage.Database,
            LegLoggerProjection.ProjectionName, tenantId: alpha, CancellationToken.None);

        await using (var qa = _store.QuerySession(alpha))
        {
            (await qa.Query<LegLog>().ToListAsync())
                .ShouldBeEmpty("alpha's docs must be wiped by the tenant-scoped teardown");
        }
        await using (var qb = _store.QuerySession(beta))
        {
            (await qb.Query<LegLog>().ToListAsync()).Count.ShouldBe(2,
                "beta's docs must survive — the teardown is tenant-scoped to alpha");
        }

        // Rebuild ONLY alpha. The per-tenant overload routes the rebuild to a
        // tenant-scoped shard; beta is not touched.
        using (var daemon = await _store.BuildProjectionDaemonAsync())
        {
            await daemon.RebuildProjectionAsync(
                LegLoggerProjection.ProjectionName, alpha, CancellationToken.None);
        }

        await using (var qa = _store.QuerySession(alpha))
        {
            var alphaLogs = await qa.Query<LegLog>().ToListAsync();
            alphaLogs.Count.ShouldBe(2,
                "alpha was rebuilt — its 2 LegEvents must materialize 2 LegLog docs");
            alphaLogs.Select(l => l.Distance).OrderBy(d => d).ShouldBe(new[] { 10.0, 20.0 });
        }
        await using (var qb = _store.QuerySession(beta))
        {
            (await qb.Query<LegLog>().ToListAsync()).Count.ShouldBe(2,
                "beta's docs must STILL be 2 — the per-tenant rebuild for alpha did not touch beta");
        }
    }
}

public record LegEvent(double Distance);

public class LegLog
{
    public Guid Id { get; set; }
    public double Distance { get; set; }
}

public partial class LegLoggerProjection : EventProjection
{
    public const string ProjectionName = "LegLogger";

    public LegLoggerProjection()
    {
        Name = ProjectionName;

        // EventProjection (vs Single/MultiStreamProjection) does NOT auto-register
        // its output document types as teardown targets — the source generator
        // surfaces them in AsyncOptions.StorageTypes (for schema build-out) but
        // CleanUps stays empty unless the projection declares it. Without this,
        // DeleteProjectionProgressAsync(name, tenantId, …) deletes the progression
        // row only — projected docs survive and a subsequent rebuild double-writes.
        Options.DeleteViewTypeOnTeardown<LegLog>();
    }

    public LegLog Create(IEvent<LegEvent> e) => new()
    {
        Id = e.Id,
        Distance = e.Data.Distance
    };
}
