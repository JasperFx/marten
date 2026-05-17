using System;
using System.Threading.Tasks;
using Marten;
using Marten.Exceptions;
using Marten.Internal.ClosedShape;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests.Storage.Identification;

/// <summary>
/// W3 spike M5: conjoined-multi-tenancy on the closed-shape document
/// storage. Verifies that the same id is allowed in two tenants
/// independently (the PK is (tenant_id, id), not just (id)), that
/// loads are tenant-scoped, and that Update / Insert exception
/// semantics still hold per-tenant.
/// </summary>
public class closed_shape_storage_tenancy_tests: BugIntegrationContext
{
    private DocumentStore ConjoinedStore()
    {
        return StoreOptions(opts =>
        {
            opts.Schema.For<TenantDoc>().MultiTenanted();
        });
    }

    [Fact]
    public async Task store_and_load_round_trips_within_a_tenant()
    {
        var store = ConjoinedStore();
        store.UseLightweightSequentialGuidClosedShape<TenantDoc>();

        var id = Guid.NewGuid();
        await using (var session = store.LightweightSession("tenantA"))
        {
            session.Store(new TenantDoc { Id = id, Name = "alice" });
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession("tenantA");
        var loaded = await query.LoadAsync<TenantDoc>(id);
        loaded.ShouldNotBeNull();
        loaded.Name.ShouldBe("alice");
    }

    [Fact]
    public async Task same_id_can_exist_in_two_different_tenants()
    {
        var store = ConjoinedStore();
        store.UseLightweightSequentialGuidClosedShape<TenantDoc>();

        var id = Guid.NewGuid();

        // Insert the same id into two tenants — the PK is (tenant_id, id)
        // so there's no collision.
        await using (var session = store.LightweightSession("tenantA"))
        {
            session.Insert(new TenantDoc { Id = id, Name = "alice" });
            await session.SaveChangesAsync();
        }

        await using (var session = store.LightweightSession("tenantB"))
        {
            session.Insert(new TenantDoc { Id = id, Name = "bob" });
            await session.SaveChangesAsync();
        }

        await using var queryA = store.QuerySession("tenantA");
        var fromA = await queryA.LoadAsync<TenantDoc>(id);
        fromA.ShouldNotBeNull();
        fromA.Name.ShouldBe("alice");

        await using var queryB = store.QuerySession("tenantB");
        var fromB = await queryB.LoadAsync<TenantDoc>(id);
        fromB.ShouldNotBeNull();
        fromB.Name.ShouldBe("bob");
    }

    [Fact]
    public async Task update_is_scoped_to_the_tenant()
    {
        var store = ConjoinedStore();
        store.UseLightweightSequentialGuidClosedShape<TenantDoc>();

        var id = Guid.NewGuid();

        await using (var session = store.LightweightSession("tenantA"))
        {
            session.Store(new TenantDoc { Id = id, Name = "alice-v1" });
            await session.SaveChangesAsync();
        }

        await using (var session = store.LightweightSession("tenantB"))
        {
            session.Store(new TenantDoc { Id = id, Name = "bob-v1" });
            await session.SaveChangesAsync();
        }

        // Update tenantA only.
        await using (var session = store.LightweightSession("tenantA"))
        {
            session.Update(new TenantDoc { Id = id, Name = "alice-v2" });
            await session.SaveChangesAsync();
        }

        await using var queryA = store.QuerySession("tenantA");
        (await queryA.LoadAsync<TenantDoc>(id)).Name.ShouldBe("alice-v2");

        // tenantB row untouched.
        await using var queryB = store.QuerySession("tenantB");
        (await queryB.LoadAsync<TenantDoc>(id)).Name.ShouldBe("bob-v1");
    }

    [Fact]
    public async Task update_throws_when_row_exists_only_in_a_different_tenant()
    {
        var store = ConjoinedStore();
        store.UseLightweightSequentialGuidClosedShape<TenantDoc>();

        var id = Guid.NewGuid();
        await using (var session = store.LightweightSession("tenantA"))
        {
            session.Store(new TenantDoc { Id = id, Name = "alice" });
            await session.SaveChangesAsync();
        }

        // From tenantB, no row with this id exists — the
        // "and tenant_id = ?" clause filters tenantA's row out, so the
        // update affects zero rows and raises NonExistentDocumentException.
        await using (var session = store.LightweightSession("tenantB"))
        {
            session.Update(new TenantDoc { Id = id, Name = "bob-update" });
            await Should.ThrowAsync<NonExistentDocumentException>(
                () => session.SaveChangesAsync());
        }
    }
}

public class TenantDoc
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
