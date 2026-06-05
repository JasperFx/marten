using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Storage;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace MultiTenancyTests;

/// <summary>
/// Coverage for the #4668 <c>RebuildSingleStreamAsync</c> tenant-aware
/// overloads on <see cref="Marten.AdvancedOperations"/>:
///
/// <list type="bullet">
/// <item><c>RebuildSingleStreamAsync&lt;T&gt;(Guid streamId, string tenantId, CancellationToken)</c></item>
/// <item><c>RebuildSingleStreamAsync&lt;T&gt;(string streamKey, string tenantId, CancellationToken)</c></item>
/// </list>
///
/// <para>
/// Both overloads must drive the underlying <c>AggregateStreamAsync</c> and
/// the projected-document upsert against the supplied tenant id. Two
/// tenancy shapes are exercised here:
/// </para>
///
/// <list type="bullet">
/// <item><b>Conjoined tenancy</b> — one database, multiple tenants
/// separated by <c>tenant_id</c> on <c>mt_events</c> and the projected
/// document tables.</item>
/// <item><b>Per-database multi-tenancy</b> — one database per tenant via
/// <see cref="StoreOptions.MultiTenantedWithSingleServer"/>. The rebuild
/// session must route to the right tenant database.</item>
/// </list>
///
/// <para>
/// Each test runs the rebuild for tenant A, then verifies (a) tenant A's
/// projection reflects A's events and (b) tenant B's projection is
/// unchanged from its own events — i.e. the rebuild stays scoped.
/// </para>
/// </summary>
public class rebuild_single_stream_tenant_overloads
{
    // ---- Conjoined tenancy ------------------------------------------------

    [Fact]
    public async Task conjoined_rebuild_by_guid_with_tenant_id()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "rebuild_4668_conj_guid";
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Policies.AllDocumentsAreMultiTenanted();
        });
        await store.Advanced.Clean.CompletelyRemoveAllAsync();

        // tenant A: 3 AEvents → ACount = 3 after rebuild
        // tenant B: 2 BEvents → BCount = 2 after rebuild (must NOT pick up A's events)
        var streamA = Guid.NewGuid();
        var streamB = Guid.NewGuid();

        await using (var session = store.LightweightSession("tenantA"))
        {
            session.Events.StartStream<Bug4668Aggregate>(streamA,
                new AEvent_4668(), new AEvent_4668(), new AEvent_4668());
            await session.SaveChangesAsync();
        }

        await using (var session = store.LightweightSession("tenantB"))
        {
            session.Events.StartStream<Bug4668Aggregate>(streamB,
                new BEvent_4668(), new BEvent_4668());
            await session.SaveChangesAsync();
        }

        // Rebuild tenant A's projection by Guid + tenant id.
        await store.Advanced.RebuildSingleStreamAsync<Bug4668Aggregate>(streamA, "tenantA");

        await using (var query = store.QuerySession("tenantA"))
        {
            var docA = await query.LoadAsync<Bug4668Aggregate>(streamA);
            docA.ShouldNotBeNull("tenant A's projection must materialize for the rebuilt stream");
            docA.ACount.ShouldBe(3, "tenant A's AEvents must roll up into ACount");
            docA.BCount.ShouldBe(0, "tenant A's stream has no BEvents");
        }

        // Now rebuild tenant B; the new overload must route the AggregateStream
        // read AND the upsert to tenant B's row, leaving tenant A's doc untouched.
        await store.Advanced.RebuildSingleStreamAsync<Bug4668Aggregate>(streamB, "tenantB");

        await using (var query = store.QuerySession("tenantB"))
        {
            var docB = await query.LoadAsync<Bug4668Aggregate>(streamB);
            docB.ShouldNotBeNull("tenant B's projection must materialize for the rebuilt stream");
            docB.ACount.ShouldBe(0, "tenant B's stream has no AEvents");
            docB.BCount.ShouldBe(2, "tenant B's BEvents must roll up into BCount");
        }

        // Cross-tenant isolation pin: loading tenant B's stream id from tenant A
        // (or vice versa) returns null — confirms the tenant scope held through
        // the rebuild.
        await using (var query = store.QuerySession("tenantA"))
        {
            var crossB = await query.LoadAsync<Bug4668Aggregate>(streamB);
            crossB.ShouldBeNull("tenant B's projection must NOT bleed into tenant A's view");
        }
    }

    [Fact]
    public async Task conjoined_rebuild_by_string_key_with_tenant_id()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "rebuild_4668_conj_str";
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Policies.AllDocumentsAreMultiTenanted();
        });
        await store.Advanced.Clean.CompletelyRemoveAllAsync();

        var keyA = "stream-A-" + Guid.NewGuid();
        var keyB = "stream-B-" + Guid.NewGuid();

        await using (var session = store.LightweightSession("tenantA"))
        {
            session.Events.StartStream<Bug4668KeyedAggregate>(keyA,
                new AEvent_4668(), new AEvent_4668(), new AEvent_4668(), new AEvent_4668());
            await session.SaveChangesAsync();
        }

        await using (var session = store.LightweightSession("tenantB"))
        {
            session.Events.StartStream<Bug4668KeyedAggregate>(keyB,
                new BEvent_4668());
            await session.SaveChangesAsync();
        }

        await store.Advanced.RebuildSingleStreamAsync<Bug4668KeyedAggregate>(keyA, "tenantA");

        await using (var query = store.QuerySession("tenantA"))
        {
            var docA = await query.LoadAsync<Bug4668KeyedAggregate>(keyA);
            docA.ShouldNotBeNull();
            docA.ACount.ShouldBe(4);
            docA.BCount.ShouldBe(0);
        }

        await store.Advanced.RebuildSingleStreamAsync<Bug4668KeyedAggregate>(keyB, "tenantB");

        await using (var query = store.QuerySession("tenantB"))
        {
            var docB = await query.LoadAsync<Bug4668KeyedAggregate>(keyB);
            docB.ShouldNotBeNull();
            docB.ACount.ShouldBe(0);
            docB.BCount.ShouldBe(1);
        }
    }

    // ---- Per-database multi-tenancy ---------------------------------------
    //
    // MultiTenantedWithSingleServer puts each tenant in its own physical
    // database. RebuildSingleStreamAsync's session has to route to the
    // right per-tenant database for both the event load and the upsert.

    [Fact]
    public async Task per_database_rebuild_by_guid_with_tenant_id()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(s =>
            {
                s.AddMarten(opts =>
                {
                    opts.MultiTenantedWithSingleServer(
                        ConnectionSource.ConnectionString,
                        t => t.WithTenants("rebuild4668_g_tenantA", "rebuild4668_g_tenantB"));

                    opts.DatabaseSchemaName = "rebuild_4668_perdb_guid";
                }).ApplyAllDatabaseChangesOnStartup();
            }).StartAsync();

        var store = host.Services.GetRequiredService<IDocumentStore>();

        // Each tenant gets its own DB. Reset both so prior runs don't leak.
        await store.Advanced.ResetAllData();

        var streamA = Guid.NewGuid();
        var streamB = Guid.NewGuid();

        await using (var session = store.LightweightSession("rebuild4668_g_tenantA"))
        {
            session.Events.StartStream<Bug4668Aggregate>(streamA,
                new AEvent_4668(), new AEvent_4668());
            await session.SaveChangesAsync();
        }

        await using (var session = store.LightweightSession("rebuild4668_g_tenantB"))
        {
            session.Events.StartStream<Bug4668Aggregate>(streamB,
                new BEvent_4668(), new BEvent_4668(), new BEvent_4668());
            await session.SaveChangesAsync();
        }

        await store.Advanced.RebuildSingleStreamAsync<Bug4668Aggregate>(
            streamA, "rebuild4668_g_tenantA");

        await using (var query = store.QuerySession("rebuild4668_g_tenantA"))
        {
            var doc = await query.LoadAsync<Bug4668Aggregate>(streamA);
            doc.ShouldNotBeNull("rebuild must persist into tenant A's database");
            doc.ACount.ShouldBe(2);
        }

        await store.Advanced.RebuildSingleStreamAsync<Bug4668Aggregate>(
            streamB, "rebuild4668_g_tenantB");

        await using (var query = store.QuerySession("rebuild4668_g_tenantB"))
        {
            var doc = await query.LoadAsync<Bug4668Aggregate>(streamB);
            doc.ShouldNotBeNull("rebuild must persist into tenant B's database");
            doc.BCount.ShouldBe(3);
        }

        await host.StopAsync();
    }

    [Fact]
    public async Task per_database_rebuild_by_string_key_with_tenant_id()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(s =>
            {
                s.AddMarten(opts =>
                {
                    opts.MultiTenantedWithSingleServer(
                        ConnectionSource.ConnectionString,
                        t => t.WithTenants("rebuild4668_s_tenantA", "rebuild4668_s_tenantB"));

                    opts.Events.StreamIdentity = StreamIdentity.AsString;
                    opts.DatabaseSchemaName = "rebuild_4668_perdb_str";
                }).ApplyAllDatabaseChangesOnStartup();
            }).StartAsync();

        var store = host.Services.GetRequiredService<IDocumentStore>();
        await store.Advanced.ResetAllData();

        var keyA = "stream-A-" + Guid.NewGuid();
        var keyB = "stream-B-" + Guid.NewGuid();

        await using (var session = store.LightweightSession("rebuild4668_s_tenantA"))
        {
            session.Events.StartStream<Bug4668KeyedAggregate>(keyA,
                new AEvent_4668(), new AEvent_4668(), new AEvent_4668());
            await session.SaveChangesAsync();
        }

        await using (var session = store.LightweightSession("rebuild4668_s_tenantB"))
        {
            session.Events.StartStream<Bug4668KeyedAggregate>(keyB,
                new BEvent_4668(), new BEvent_4668());
            await session.SaveChangesAsync();
        }

        await store.Advanced.RebuildSingleStreamAsync<Bug4668KeyedAggregate>(
            keyA, "rebuild4668_s_tenantA");

        await using (var query = store.QuerySession("rebuild4668_s_tenantA"))
        {
            var doc = await query.LoadAsync<Bug4668KeyedAggregate>(keyA);
            doc.ShouldNotBeNull("rebuild must persist into tenant A's database");
            doc.ACount.ShouldBe(3);
        }

        await store.Advanced.RebuildSingleStreamAsync<Bug4668KeyedAggregate>(
            keyB, "rebuild4668_s_tenantB");

        await using (var query = store.QuerySession("rebuild4668_s_tenantB"))
        {
            var doc = await query.LoadAsync<Bug4668KeyedAggregate>(keyB);
            doc.ShouldNotBeNull("rebuild must persist into tenant B's database");
            doc.BCount.ShouldBe(2);
        }

        await host.StopAsync();
    }
}

// ---- Test types ----------------------------------------------------------

public class Bug4668Aggregate
{
    public Guid Id { get; set; }
    public int ACount { get; set; }
    public int BCount { get; set; }

    public void Apply(AEvent_4668 _) => ACount++;
    public void Apply(BEvent_4668 _) => BCount++;
}

public class Bug4668KeyedAggregate
{
    public string Id { get; set; } = string.Empty;
    public int ACount { get; set; }
    public int BCount { get; set; }

    public void Apply(AEvent_4668 _) => ACount++;
    public void Apply(BEvent_4668 _) => BCount++;
}

// Suffixed with "_4668" to avoid colliding with AEvent / BEvent already
// defined elsewhere in the MultiTenancyTests project.
public record AEvent_4668;
public record BEvent_4668;
