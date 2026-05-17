using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Linq.SoftDeletes;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests.Storage.Identification;

/// <summary>
/// W3 spike M17: verifies session.Delete / DeleteWhere / HardDelete /
/// DeleteByIdAsync against the closed-shape document storage. These
/// paths route through the inherited
/// <c>DocumentStorage&lt;T, TId&gt;.DeleteFragment</c> +
/// <c>HardDeleteFragment</c> so the closed-shape doesn't need
/// hand-written delete operations — but they still need to honor
/// strong-typed-id unwrapping and conjoined-tenancy filters.
/// </summary>
public class closed_shape_delete_tests: BugIntegrationContext
{
    private DocumentStore ClosedShapeStore(Action<StoreOptions>? extra = null)
        => StoreOptions(opts =>
        {
            opts.UseClosedShapeDocumentStorage = true;
            extra?.Invoke(opts);
        });

    [Fact]
    public async Task delete_by_id_removes_the_row_for_remove_style()
    {
        var store = ClosedShapeStore();
        var id = Guid.NewGuid();
        await using (var s = store.LightweightSession())
        {
            s.Store(new DelDoc { Id = id, Name = "victim" });
            await s.SaveChangesAsync();
        }

        await using (var s = store.LightweightSession())
        {
            s.Delete<DelDoc>(id);
            await s.SaveChangesAsync();
        }

        await using var q = store.QuerySession();
        (await q.LoadAsync<DelDoc>(id)).ShouldBeNull();
    }

    [Fact]
    public async Task delete_by_document_works()
    {
        var store = ClosedShapeStore();
        var doc = new DelDoc { Id = Guid.NewGuid(), Name = "by-document" };
        await using (var s = store.LightweightSession())
        {
            s.Store(doc);
            await s.SaveChangesAsync();
        }

        await using (var s = store.LightweightSession())
        {
            s.Delete(doc);
            await s.SaveChangesAsync();
        }

        await using var q = store.QuerySession();
        (await q.LoadAsync<DelDoc>(doc.Id)).ShouldBeNull();
    }

    [Fact]
    public async Task delete_where_removes_matching_rows()
    {
        var store = ClosedShapeStore();
        await using (var s = store.LightweightSession())
        {
            s.Store(new DelDoc { Id = Guid.NewGuid(), Name = "keep" });
            s.Store(new DelDoc { Id = Guid.NewGuid(), Name = "drop" });
            s.Store(new DelDoc { Id = Guid.NewGuid(), Name = "drop" });
            await s.SaveChangesAsync();
        }

        await using (var s = store.LightweightSession())
        {
            s.DeleteWhere<DelDoc>(x => x.Name == "drop");
            await s.SaveChangesAsync();
        }

        await using var q = store.QuerySession();
        var remaining = await q.Query<DelDoc>().ToListAsync();
        remaining.Count.ShouldBe(1);
        remaining[0].Name.ShouldBe("keep");
    }

    [Fact]
    public async Task soft_delete_hard_delete_removes_the_row()
    {
        var store = ClosedShapeStore(opts =>
        {
            opts.Schema.For<DelSoftDoc>().SoftDeleted();
        });

        var id = Guid.NewGuid();
        await using (var s = store.LightweightSession())
        {
            s.Store(new DelSoftDoc { Id = id, Name = "soft" });
            await s.SaveChangesAsync();
        }

        // First Delete tombstones the row (still in DB).
        await using (var s = store.LightweightSession())
        {
            s.Delete<DelSoftDoc>(id);
            await s.SaveChangesAsync();
        }

        await using (var q = store.QuerySession())
        {
            // Tombstoned but still loadable via LoadAsync.
            (await q.LoadAsync<DelSoftDoc>(id)).ShouldNotBeNull();
        }

        // HardDelete physically removes it.
        await using (var s = store.LightweightSession())
        {
            s.HardDelete<DelSoftDoc>(id);
            await s.SaveChangesAsync();
        }

        await using (var q = store.QuerySession())
        {
            (await q.LoadAsync<DelSoftDoc>(id)).ShouldBeNull();
        }
    }

    [Fact]
    public async Task delete_with_conjoined_tenancy_is_scoped()
    {
        var store = ClosedShapeStore(opts =>
        {
            opts.Schema.For<DelTenantDoc>().MultiTenanted();
        });

        var id = Guid.NewGuid();
        await using (var s = store.LightweightSession("tenantA"))
        {
            s.Store(new DelTenantDoc { Id = id, Name = "A" });
            await s.SaveChangesAsync();
        }

        await using (var s = store.LightweightSession("tenantB"))
        {
            s.Store(new DelTenantDoc { Id = id, Name = "B" });
            await s.SaveChangesAsync();
        }

        // Delete only from tenantA.
        await using (var s = store.LightweightSession("tenantA"))
        {
            s.Delete<DelTenantDoc>(id);
            await s.SaveChangesAsync();
        }

        await using (var q = store.QuerySession("tenantA"))
            (await q.LoadAsync<DelTenantDoc>(id)).ShouldBeNull();
        await using (var q = store.QuerySession("tenantB"))
            (await q.LoadAsync<DelTenantDoc>(id))!.Name.ShouldBe("B");
    }

    [Fact]
    public async Task delete_with_strong_typed_id_unwraps_to_inner()
    {
        var store = ClosedShapeStore(opts =>
        {
            opts.RegisterValueType(typeof(DelStrongId));
        });

        var doc = new DelStrongIdDoc { Name = "strong" };
        await using (var s = store.LightweightSession())
        {
            s.Store(doc);
            await s.SaveChangesAsync();
        }

        await using (var s = store.LightweightSession())
        {
            s.Delete<DelStrongIdDoc>(doc.Id);
            await s.SaveChangesAsync();
        }

        await using var q = store.QuerySession();
        (await q.LoadAsync<DelStrongIdDoc>(doc.Id)).ShouldBeNull();
    }
}

public class DelDoc
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class DelSoftDoc
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class DelTenantDoc
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public record struct DelStrongId(Guid Value);

public class DelStrongIdDoc
{
    public DelStrongId Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
