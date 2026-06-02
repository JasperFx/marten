using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Metadata;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

/// <summary>
/// <c>Query&lt;T&gt;()</c> throws <see cref="IndexOutOfRangeException"/> ("Ordinal must be
/// between 0 and 1") when a document is BOTH tenant-mapped (the <c>tenant_id</c> metadata
/// column is projected onto a member — via <see cref="ITenanted"/> or
/// <c>.Metadata(m =&gt; m.TenantId.MapTo(...))</c>) AND a projection/aggregate target (so it is
/// optimistic-concurrency-versioned with no <c>[Version]</c> member).
/// </summary>
/// <remarks>
/// Either feature alone is fine (see the two <c>control_*</c> tests), and <c>LoadAsync&lt;T&gt;()</c>
/// is unaffected — only the LINQ path breaks. Likely cause: <c>DocumentStorageDescriptorBuilder.Build</c>
/// adds a version read-binder whenever <c>UseOptimisticConcurrency</c> is set, without the
/// <c>storageStyle != QueryOnly</c> guard that <c>VersionColumn.ShouldSelect</c> applies. The single
/// descriptor is shared across storage styles (<c>ClosedShapeRegistration.BuildProvider</c>), so the
/// <c>QueryOnly</c> selector ends up with one more metadata read-binder than its SELECT projects,
/// shifting every later ordinal so the tenant binder reads past the end of the row.
/// </remarks>
public partial class Bug_4602_query_tenant_mapped_projection_document : BugIntegrationContext
{
    [Fact]
    public async Task can_query_an_ITenanted_projection_document()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Projections.Add<TenantedProjection>(ProjectionLifecycle.Inline);
        });

        var id = Guid.NewGuid();
        await using (var session = theStore.LightweightSession("tenant-a"))
        {
            session.Events.StartStream<TenantedDoc>(id, new TenantedCreated(id, "tenanted"));
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession("tenant-a");

        // sanity: the load-by-id path works
        (await query.LoadAsync<TenantedDoc>(id)).ShouldNotBeNull();

        // bug: throws IndexOutOfRangeException: "Ordinal must be between 0 and 1"
        var docs = await query.Query<TenantedDoc>().ToListAsync();
        docs.Count.ShouldBe(1);
    }

    [Fact]
    public async Task can_query_a_MapTo_projection_document()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Projections.Add<MappedProjection>(ProjectionLifecycle.Inline);
            opts.Schema.For<MappedDoc>()
                .MultiTenanted()
                .Metadata(m => m.TenantId.MapTo(x => x.Tenant));
        });

        var id = Guid.NewGuid();
        await using (var session = theStore.LightweightSession("tenant-a"))
        {
            session.Events.StartStream<MappedDoc>(id, new MappedCreated(id, "mapped"));
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession("tenant-a");

        (await query.LoadAsync<MappedDoc>(id)).ShouldNotBeNull();

        var docs = await query.Query<MappedDoc>().ToListAsync();
        docs.Count.ShouldBe(1);
    }

    [Fact]
    public async Task workaround_IRevisioned_member_realigns_the_select()
    {
        // Userland escape hatch — and the sharpest confirmation of the root cause.
        // A projection target gets UseNumericRevisions=true with no revision member, so the
        // QueryOnly SELECT omits mt_version while the shared descriptor still binds it (the bug).
        // Implementing IRevisioned gives the revision column an actual member, so
        // RevisionColumn.ShouldSelect returns true for QueryOnly too: the SELECT now includes
        // mt_version, the column/binder counts realign, and the LINQ path stops throwing.
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Projections.Add<RevisionedProjection>(ProjectionLifecycle.Inline);
            opts.Schema.For<RevisionedDoc>()
                .MultiTenanted()
                .Metadata(m => m.TenantId.MapTo(x => x.Tenant));
        });

        var id = Guid.NewGuid();
        await using (var session = theStore.LightweightSession("tenant-a"))
        {
            session.Events.StartStream<RevisionedDoc>(id, new RevisionedCreated(id, "revisioned"));
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession("tenant-a");

        (await query.LoadAsync<RevisionedDoc>(id)).ShouldNotBeNull();

        var docs = await query.Query<RevisionedDoc>().ToListAsync();
        docs.Count.ShouldBe(1);
    }

    // ---- controls: each feature ALONE already works; kept so a partial fix can't shift the boundary ----

    [Fact]
    public async Task control_tenant_mapped_but_not_a_projection_target()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<MappedDoc>()
                .MultiTenanted()
                .Metadata(m => m.TenantId.MapTo(x => x.Tenant));
        });

        var id = Guid.NewGuid();
        await using (var session = theStore.LightweightSession("tenant-a"))
        {
            session.Store(new MappedDoc { Id = id, Name = "stored" });
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession("tenant-a");
        (await query.Query<MappedDoc>().ToListAsync()).Count.ShouldBe(1);
    }

    [Fact]
    public async Task control_projection_target_but_not_tenant_mapped()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Projections.Add<PlainProjection>(ProjectionLifecycle.Inline);
            // Conjoined events require a conjoined projection target, but with no
            // tenant member mapped — so there's no DocumentTenantIdBinder, isolating
            // the version-binder offset from the tenant read.
            opts.Schema.For<PlainDoc>().MultiTenanted();
        });

        var id = Guid.NewGuid();
        await using (var session = theStore.LightweightSession("tenant-a"))
        {
            session.Events.StartStream<PlainDoc>(id, new PlainCreated(id, "plain"));
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession("tenant-a");
        (await query.Query<PlainDoc>().ToListAsync()).Count.ShouldBe(1);
    }

    // ---- types ----

    // One event type per projection so each read model populates from exactly its own stream.
    public record PlainCreated(Guid Id, string Name);
    public record MappedCreated(Guid Id, string Name);
    public record TenantedCreated(Guid Id, string Name);
    public record RevisionedCreated(Guid Id, string Name);

    public class PlainDoc
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
    }

    public class MappedDoc
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string Tenant { get; set; } = "";
    }

    public class TenantedDoc : ITenanted
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string? TenantId { get; set; }
    }

    public class RevisionedDoc : IRevisioned
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string Tenant { get; set; } = "";
        public int Version { get; set; }
    }

    public partial class PlainProjection : SingleStreamProjection<PlainDoc, Guid>
    {
        public static PlainDoc Create(IEvent<PlainCreated> e) => new() { Id = e.Data.Id, Name = e.Data.Name };
    }

    public partial class MappedProjection : SingleStreamProjection<MappedDoc, Guid>
    {
        public static MappedDoc Create(IEvent<MappedCreated> e) => new() { Id = e.Data.Id, Name = e.Data.Name };
    }

    public partial class TenantedProjection : SingleStreamProjection<TenantedDoc, Guid>
    {
        public static TenantedDoc Create(IEvent<TenantedCreated> e) => new() { Id = e.Data.Id, Name = e.Data.Name };
    }

    public partial class RevisionedProjection : SingleStreamProjection<RevisionedDoc, Guid>
    {
        public static RevisionedDoc Create(IEvent<RevisionedCreated> e) => new() { Id = e.Data.Id, Name = e.Data.Name };
    }
}
