using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_4947_for_tenant_tenantless_documents: BugIntegrationContext
{
    public class GlobalDoc
    {
        public Guid Id { get; set; }
        public string Label { get; set; }
    }

    public class TenantedDoc
    {
        public Guid Id { get; set; }
        public string Label { get; set; }
    }

    [Fact]
    public async Task pending_tenantless_document_is_visible_through_for_tenant()
    {
        // The exact scenario from GH-4947: identity session, Store() a tenancy-neutral
        // document (not yet committed), then read it back through ForTenant()
        await using var session = theStore.IdentitySession();

        var doc = new GlobalDoc { Id = Guid.NewGuid(), Label = "foo" };
        session.Store(doc);

        var loaded = await session.ForTenant("bar").LoadAsync<GlobalDoc>(doc.Id);

        loaded.ShouldNotBeNull();
        loaded.ShouldBeSameAs(doc);
    }

    [Fact]
    public async Task pending_tenantless_document_is_visible_through_load_many_for_tenant()
    {
        await using var session = theStore.IdentitySession();

        var one = new GlobalDoc { Id = Guid.NewGuid(), Label = "one" };
        var two = new GlobalDoc { Id = Guid.NewGuid(), Label = "two" };
        session.Store(one, two);

        var loaded = await session.ForTenant("bar").LoadManyAsync<GlobalDoc>(one.Id, two.Id);

        loaded.Count.ShouldBe(2);
        loaded.OrderBy(x => x.Label).Select(x => x.Label).ShouldBe(new[] { "one", "two" });
    }

    [Fact]
    public async Task committed_tenantless_document_resolves_to_the_same_instance_across_tenants()
    {
        var id = Guid.NewGuid();

        await using (var seed = theStore.LightweightSession())
        {
            seed.Store(new GlobalDoc { Id = id, Label = "global" });
            await seed.SaveChangesAsync();
        }

        await using var session = theStore.IdentitySession();

        var viaA = await session.ForTenant("a").LoadAsync<GlobalDoc>(id);
        viaA.ShouldNotBeNull();
        viaA.Label.ShouldBe("global");

        var viaB = await session.ForTenant("b").LoadAsync<GlobalDoc>(id);
        viaB.ShouldNotBeNull();

        // One document, one identity map entry -- every ForTenant view of the same identity
        // session (and the session itself) must resolve the same tracked instance
        viaB.ShouldBeSameAs(viaA);

        var direct = await session.LoadAsync<GlobalDoc>(id);
        direct.ShouldBeSameAs(viaA);
    }

    [Fact]
    public async Task tenantless_document_stored_through_for_tenant_is_visible_to_the_parent_session()
    {
        await using var session = theStore.IdentitySession();

        var doc = new GlobalDoc { Id = Guid.NewGuid(), Label = "foo" };
        session.ForTenant("a").Store(doc);

        (await session.LoadAsync<GlobalDoc>(doc.Id)).ShouldBeSameAs(doc);
        (await session.ForTenant("b").LoadAsync<GlobalDoc>(doc.Id)).ShouldBeSameAs(doc);
    }

    [Fact]
    public async Task conjoined_documents_stay_isolated_per_tenant()
    {
        // Guard rail for #4801: a conjoined document must NOT be shared across ForTenant views
        StoreOptions(opts => opts.Schema.For<TenantedDoc>().MultiTenanted());

        var id = Guid.NewGuid();

        await using (var seed = theStore.LightweightSession("a"))
        {
            seed.Store(new TenantedDoc { Id = id, Label = "tenant-a" });
            await seed.SaveChangesAsync();
        }

        await using (var seed = theStore.LightweightSession("b"))
        {
            seed.Store(new TenantedDoc { Id = id, Label = "tenant-b" });
            await seed.SaveChangesAsync();
        }

        await using var session = theStore.IdentitySession("a");

        var a = await session.ForTenant("a").LoadAsync<TenantedDoc>(id);
        a.Label.ShouldBe("tenant-a");

        var b = await session.ForTenant("b").LoadAsync<TenantedDoc>(id);
        b.Label.ShouldBe("tenant-b");

        b.ShouldNotBeSameAs(a);
    }

    [Fact]
    public async Task mixed_store_shares_only_the_tenancy_neutral_document()
    {
        StoreOptions(opts => opts.Schema.For<TenantedDoc>().MultiTenanted());

        var globalId = Guid.NewGuid();
        var tenantedId = Guid.NewGuid();

        await using (var seed = theStore.LightweightSession("a"))
        {
            seed.Store(new GlobalDoc { Id = globalId, Label = "global" });
            seed.Store(new TenantedDoc { Id = tenantedId, Label = "tenant-a" });
            await seed.SaveChangesAsync();
        }

        await using (var seed = theStore.LightweightSession("b"))
        {
            seed.Store(new TenantedDoc { Id = tenantedId, Label = "tenant-b" });
            await seed.SaveChangesAsync();
        }

        await using var session = theStore.IdentitySession("a");

        var globalViaA = await session.ForTenant("a").LoadAsync<GlobalDoc>(globalId);
        var tenantedViaA = await session.ForTenant("a").LoadAsync<TenantedDoc>(tenantedId);

        var globalViaB = await session.ForTenant("b").LoadAsync<GlobalDoc>(globalId);
        var tenantedViaB = await session.ForTenant("b").LoadAsync<TenantedDoc>(tenantedId);

        globalViaB.ShouldBeSameAs(globalViaA);

        tenantedViaA.Label.ShouldBe("tenant-a");
        tenantedViaB.Label.ShouldBe("tenant-b");
    }
}
