using System;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_4801_identity_map_cross_tenant_load: BugIntegrationContext
{
    public class Doc
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
    }

    private async Task configure()
    {
        StoreOptions(opts =>
        {
            opts.Policies.AllDocumentsAreMultiTenanted();
        });
    }

    [Fact]
    public async Task optimistic_concurrency_version_tracking_is_tenant_scoped()
    {
        StoreOptions(opts =>
        {
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Schema.For<Doc>().UseOptimisticConcurrency(true);
        });

        var id = Guid.NewGuid();

        await using (var seed = theStore.LightweightSession("A"))
        {
            seed.Store(new Doc { Id = id, Name = "tenant-A" });
            await seed.SaveChangesAsync();
        }

        await using (var seed = theStore.LightweightSession("B"))
        {
            // Independent inserts under conjoined tenancy produce different mt_version
            // values for the same id per tenant.
            seed.Store(new Doc { Id = id, Name = "tenant-B" });
            await seed.SaveChangesAsync();
        }

        await using var session = theStore.DirtyTrackedSession("A");

        var a = await session.ForTenant("A").LoadAsync<Doc>(id);
        // Loading B into the same (previously shared) version tracker overwrote A's
        // tracked version keyed by id.
        await session.ForTenant("B").LoadAsync<Doc>(id);

        a.Name = "tenant-A-updated";
        session.ForTenant("A").Update(a);

        // With a tenant-blind shared version tracker this asserts B's version and throws
        // a spurious ConcurrencyException.
        await Should.NotThrowAsync(() => session.SaveChangesAsync());
    }

    [Fact]
    public async Task load_across_tenants_via_for_tenant_dirty_tracking()
    {
        await configure();

        var id = Guid.NewGuid();

        // Seed the same id with different content for two tenants
        await using (var seed = theStore.LightweightSession("A"))
        {
            seed.Store(new Doc { Id = id, Name = "tenant-A" });
            await seed.SaveChangesAsync();
        }

        await using (var seed = theStore.LightweightSession("B"))
        {
            seed.Store(new Doc { Id = id, Name = "tenant-B" });
            await seed.SaveChangesAsync();
        }

        await using var session = theStore.DirtyTrackedSession("A");

        var a = await session.ForTenant("A").LoadAsync<Doc>(id);
        a.ShouldNotBeNull();
        a.Name.ShouldBe("tenant-A");

        var b = await session.ForTenant("B").LoadAsync<Doc>(id);
        b.ShouldNotBeNull();
        b.Name.ShouldBe("tenant-B");
    }

    [Fact]
    public async Task store_across_tenants_does_not_overwrite_other_tenant_cache()
    {
        await configure();

        var id = Guid.NewGuid();

        await using (var seed = theStore.LightweightSession("A"))
        {
            seed.Store(new Doc { Id = id, Name = "tenant-A" });
            await seed.SaveChangesAsync();
        }

        await using var session = theStore.DirtyTrackedSession("A");

        // Cache tenant A's instance in A's map
        var a = await session.ForTenant("A").LoadAsync<Doc>(id);
        a.Name.ShouldBe("tenant-A");

        // Storing a new instance for the same id under tenant B must not clobber A's cache
        session.ForTenant("B").Store(new Doc { Id = id, Name = "tenant-B" });

        var stillA = await session.ForTenant("A").LoadAsync<Doc>(id);
        stillA.Name.ShouldBe("tenant-A");
    }

    [Fact]
    public async Task delete_via_for_tenant_ejects_only_that_tenants_cache()
    {
        await configure();

        var id = Guid.NewGuid();

        await using (var seed = theStore.LightweightSession("A"))
        {
            seed.Store(new Doc { Id = id, Name = "tenant-A" });
            await seed.SaveChangesAsync();
        }

        await using (var seed = theStore.LightweightSession("B"))
        {
            seed.Store(new Doc { Id = id, Name = "tenant-B" });
            await seed.SaveChangesAsync();
        }

        await using var session = theStore.DirtyTrackedSession("A");

        (await session.ForTenant("A").LoadAsync<Doc>(id)).Name.ShouldBe("tenant-A");
        (await session.ForTenant("B").LoadAsync<Doc>(id)).Name.ShouldBe("tenant-B");

        // Delete ejects id from A's own identity map; B's cached instance must be untouched
        session.ForTenant("A").Delete<Doc>(id);

        (await session.ForTenant("B").LoadAsync<Doc>(id)).Name.ShouldBe("tenant-B");
    }

    [Fact]
    public async Task for_tenant_matching_the_sessions_own_tenant_shares_the_identity_map()
    {
        await configure();

        var id = Guid.NewGuid();

        await using (var seed = theStore.LightweightSession("A"))
        {
            seed.Store(new Doc { Id = id, Name = "tenant-A" });
            await seed.SaveChangesAsync();
        }

        await using var session = theStore.DirtyTrackedSession("A");

        // Loading through the session and through ForTenant for the SAME tenant must
        // resolve to the same tracked instance (shared identity map).
        var direct = await session.LoadAsync<Doc>(id);
        var viaForTenant = await session.ForTenant("A").LoadAsync<Doc>(id);

        viaForTenant.ShouldBeSameAs(direct);
    }

    [Fact]
    public async Task load_across_tenants_via_for_tenant_identity_only()
    {
        await configure();

        var id = Guid.NewGuid();

        await using (var seed = theStore.LightweightSession("A"))
        {
            seed.Store(new Doc { Id = id, Name = "tenant-A" });
            await seed.SaveChangesAsync();
        }

        await using (var seed = theStore.LightweightSession("B"))
        {
            seed.Store(new Doc { Id = id, Name = "tenant-B" });
            await seed.SaveChangesAsync();
        }

        await using var session = theStore.IdentitySession("A");

        var a = await session.ForTenant("A").LoadAsync<Doc>(id);
        a.ShouldNotBeNull();
        a.Name.ShouldBe("tenant-A");

        var b = await session.ForTenant("B").LoadAsync<Doc>(id);
        b.ShouldNotBeNull();
        b.Name.ShouldBe("tenant-B");
    }
}
