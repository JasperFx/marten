using System;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Bugs;

// #4956 — a ForTenant() view of an identity session that Store()s a CONJOINED (MultiTenanted)
// document under one tenant, then reads it back through the BASE session (a *different*
// tenant). Reported as a regression from 9.12.0: the base session no longer "sees" the doc.
//
// This is the correct behavior for conjoined tenancy and the intended effect of #4801/#4947:
// a conjoined id means a *different* document per tenant, so a session bound to tenant X must
// not resolve a document stored for tenant Y. The pre-9.13 behavior leaked one tenant's
// uncommitted write into another tenant's view through a tenant-blind identity map -- an
// in-memory answer a real database query would never give. These tests lock the intended
// ForTenant() matrix so the isolation and the legitimate sharing cases can't silently drift.
public class Bug_4956_for_tenant_conjoined_isolation: BugIntegrationContext
{
    public class TestEntity
    {
        public Guid Id { get; set; }
        public string Label { get; set; } = null!;
        public string TenantId { get; set; } = "none";
    }

    private void ConjoinedStore() => StoreOptions(opts => opts.Schema.For<TestEntity>().MultiTenanted());

    // The reporter's exact scenario. The base session is the default tenant; the write went to
    // tenant "t1". A cross-tenant read of a conjoined document must NOT resolve it.
    [Fact]
    public async Task base_session_cannot_see_other_tenants_pending_conjoined_write()
    {
        ConjoinedStore();

        await using var session = theStore.IdentitySession();

        var item1 = new TestEntity { Id = Guid.NewGuid(), Label = "foo1", TenantId = "t1" };
        session.ForTenant("t1").Store(item1);

        (await session.LoadAsync<TestEntity>(item1.Id)).ShouldBeNull();
    }

    // The correct pattern: read a conjoined document back through the SAME tenant view it was
    // stored under. ForTenant() caches one nested session per tenant, so the pending write is
    // in that view's own identity map.
    [Fact]
    public async Task same_tenant_view_sees_its_own_pending_conjoined_write()
    {
        ConjoinedStore();

        await using var session = theStore.IdentitySession();

        var item1 = new TestEntity { Id = Guid.NewGuid(), Label = "foo1", TenantId = "t1" };
        session.ForTenant("t1").Store(item1);

        var res = await session.ForTenant("t1").LoadAsync<TestEntity>(item1.Id);

        res.ShouldNotBeNull();
        res.ShouldBeSameAs(item1);
    }

    // Prove the in-memory isolation matches what the database actually holds: after committing
    // through the base session, a FRESH default-tenant session still cannot load the t1 doc,
    // while a fresh ForTenant("t1") view can. i.e. #4956 is not an identity-map artifact.
    [Fact]
    public async Task committed_conjoined_write_is_only_visible_to_its_own_tenant()
    {
        ConjoinedStore();

        var id = Guid.NewGuid();

        await using (var session = theStore.IdentitySession())
        {
            session.ForTenant("t1").Store(new TestEntity { Id = id, Label = "foo1", TenantId = "t1" });
            await session.SaveChangesAsync();
        }

        await using var fresh = theStore.IdentitySession();

        (await fresh.LoadAsync<TestEntity>(id)).ShouldBeNull();

        var viaTenant = await fresh.ForTenant("t1").LoadAsync<TestEntity>(id);
        viaTenant.ShouldNotBeNull();
        viaTenant.Label.ShouldBe("foo1");
    }

    // Storing several tenants' conjoined docs through ForTenant() on one session is a single
    // unit of work (the nested sessions share the parent work tracker): base SaveChanges()
    // commits them all, each to its own tenant.
    [Fact]
    public async Task multiple_for_tenant_stores_all_commit_through_base_save_changes()
    {
        ConjoinedStore();

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        await using (var session = theStore.IdentitySession())
        {
            session.ForTenant("t1").Store(new TestEntity { Id = id1, Label = "foo1", TenantId = "t1" });
            session.ForTenant("t2").Store(new TestEntity { Id = id2, Label = "foo2", TenantId = "t2" });
            await session.SaveChangesAsync();
        }

        await using var fresh = theStore.IdentitySession();

        (await fresh.ForTenant("t1").LoadAsync<TestEntity>(id1)).Label.ShouldBe("foo1");
        (await fresh.ForTenant("t2").LoadAsync<TestEntity>(id2)).Label.ShouldBe("foo2");

        // and no cross-tenant bleed
        (await fresh.ForTenant("t2").LoadAsync<TestEntity>(id1)).ShouldBeNull();
        (await fresh.ForTenant("t1").LoadAsync<TestEntity>(id2)).ShouldBeNull();
    }

    // The legitimate sharing case: when the ForTenant tenant MATCHES the base session's own
    // tenant, the identity map is shared, so a pending write through either path is visible to
    // the other. This is the scenario the reporter almost certainly wanted.
    [Fact]
    public async Task for_tenant_matching_base_tenant_shares_pending_writes_both_directions()
    {
        ConjoinedStore();

        await using var session = theStore.IdentitySession("t1");

        var viaTenant = new TestEntity { Id = Guid.NewGuid(), Label = "via-tenant", TenantId = "t1" };
        session.ForTenant("t1").Store(viaTenant);
        (await session.LoadAsync<TestEntity>(viaTenant.Id)).ShouldBeSameAs(viaTenant);

        var viaBase = new TestEntity { Id = Guid.NewGuid(), Label = "via-base", TenantId = "t1" };
        session.Store(viaBase);
        (await session.ForTenant("t1").LoadAsync<TestEntity>(viaBase.Id)).ShouldBeSameAs(viaBase);
    }
}
