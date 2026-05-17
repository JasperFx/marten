using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Linq.SoftDeletes;
using Marten.Metadata;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests.Storage.Identification;

/// <summary>
/// W3 spike M9: validates soft-delete behavior on the closed-shape
/// document storage. The schema picks up <c>mt_deleted</c> +
/// <c>mt_deleted_at</c> columns; writes default them to <c>false / null</c>;
/// <c>session.Delete</c> emits the soft-delete UPDATE inherited from
/// <c>DocumentStorage&lt;T, TId&gt;.DeleteFragment</c>; LINQ queries
/// filter out deleted rows by default while <c>LoadAsync</c> still
/// returns them.
/// </summary>
public class closed_shape_soft_delete_tests: BugIntegrationContext
{
    private DocumentStore SoftDeleteStore()
        => StoreOptions(opts =>
        {
            opts.UseClosedShapeDocumentStorage = true;
            opts.Schema.For<SdDoc>().SoftDeleted();
        });

    [Fact]
    public async Task fresh_documents_are_not_soft_deleted()
    {
        var store = SoftDeleteStore();

        var id = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Store(new SdDoc { Id = id, Name = "fresh" });
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        var loaded = await query.LoadAsync<SdDoc>(id);
        loaded.ShouldNotBeNull();
        loaded.Deleted.ShouldBeFalse();
        loaded.DeletedAt.ShouldBeNull();
    }

    [Fact]
    public async Task delete_soft_deletes_instead_of_removing_the_row()
    {
        var store = SoftDeleteStore();

        var id = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Store(new SdDoc { Id = id, Name = "alive" });
            await session.SaveChangesAsync();
        }

        await using (var session = store.LightweightSession())
        {
            session.Delete<SdDoc>(id);
            await session.SaveChangesAsync();
        }

        // LoadAsync ignores soft-delete by design — returns even
        // tombstoned rows so callers can inspect them.
        await using var query = store.QuerySession();
        var loaded = await query.LoadAsync<SdDoc>(id);
        loaded.ShouldNotBeNull();
        loaded.Deleted.ShouldBeTrue();
        loaded.DeletedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task linq_queries_exclude_deleted_rows_by_default()
    {
        var store = SoftDeleteStore();

        var alive = Guid.NewGuid();
        var tombstoned = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Store(new SdDoc { Id = alive, Name = "alive" });
            session.Store(new SdDoc { Id = tombstoned, Name = "tombstoned" });
            await session.SaveChangesAsync();
        }

        await using (var session = store.LightweightSession())
        {
            session.Delete<SdDoc>(tombstoned);
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        var liveIds = await query.Query<SdDoc>().Select(x => x.Id).ToListAsync();
        liveIds.ShouldHaveSingleItem();
        liveIds.ShouldContain(alive);
    }

    [Fact]
    public async Task linq_query_can_include_deleted_via_MaybeDeleted()
    {
        var store = SoftDeleteStore();

        var alive = Guid.NewGuid();
        var tombstoned = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Store(new SdDoc { Id = alive, Name = "alive" });
            session.Store(new SdDoc { Id = tombstoned, Name = "tombstoned" });
            await session.SaveChangesAsync();
        }

        await using (var session = store.LightweightSession())
        {
            session.Delete<SdDoc>(tombstoned);
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        var allIds = await query.Query<SdDoc>()
            .Where(x => x.MaybeDeleted())
            .Select(x => x.Id)
            .ToListAsync();
        allIds.Count.ShouldBe(2);
        allIds.ShouldContain(alive);
        allIds.ShouldContain(tombstoned);
    }

    [Fact]
    public async Task re_saving_a_soft_deleted_document_undeletes_it()
    {
        var store = SoftDeleteStore();

        var id = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Store(new SdDoc { Id = id, Name = "alive" });
            await session.SaveChangesAsync();
        }

        await using (var session = store.LightweightSession())
        {
            session.Delete<SdDoc>(id);
            await session.SaveChangesAsync();
        }

        // Re-save — codegen behavior is to undelete via the
        // ON CONFLICT DO UPDATE SET resetting the column to false.
        await using (var session = store.LightweightSession())
        {
            session.Store(new SdDoc { Id = id, Name = "back-alive" });
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        var loaded = await query.LoadAsync<SdDoc>(id);
        loaded.ShouldNotBeNull();
        loaded.Deleted.ShouldBeFalse();
        loaded.DeletedAt.ShouldBeNull();
        loaded.Name.ShouldBe("back-alive");
    }
}

public class SdDoc: ISoftDeleted
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Deleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
