using System;
using System.Threading.Tasks;
using JasperFx;
using Marten;
using Marten.Exceptions;
using Marten.Metadata;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests.Storage.Identification;

/// <summary>
/// W3 spike M8: validates monotonic-bigint revision behavior on the
/// closed-shape document storage. UseNumericRevisions on the mapping
/// turns on <c>mt_version</c> (bigint) with auto-increment when the
/// caller passes <c>Revision = 0</c>, explicit revisions otherwise.
/// Lower-or-equal revisions raise <see cref="ConcurrencyException"/>
/// unless <c>TryUpdateRevision</c> swallows the violation.
/// </summary>
public class closed_shape_numeric_revisions_tests: BugIntegrationContext
{
    private DocumentStore RevisionedStore()
        => StoreOptions(opts =>
        {
            opts.UseClosedShapeDocumentStorage = true;
            opts.Schema.For<RevDoc>().UseNumericRevisions(true);
        });

    [Fact]
    public async Task auto_increment_assigns_revision_one_to_a_brand_new_row()
    {
        var store = RevisionedStore();

        var doc = new RevDoc { Id = Guid.NewGuid(), Name = "first" };
        await using var session = store.LightweightSession();
        session.Store(doc);
        await session.SaveChangesAsync();

        // The operation writes the new revision back onto the document
        // via the [Version] member.
        doc.Version.ShouldBe(1);
    }

    [Fact]
    public async Task auto_increment_advances_revision_on_each_subsequent_save()
    {
        var store = RevisionedStore();

        var id = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Store(new RevDoc { Id = id, Name = "v1" });
            await session.SaveChangesAsync();
        }

        long revisionAfterSecond;
        await using (var session = store.LightweightSession())
        {
            var doc = new RevDoc { Id = id, Name = "v2" };
            session.Store(doc);
            await session.SaveChangesAsync();
            revisionAfterSecond = doc.Version;
        }

        revisionAfterSecond.ShouldBe(2);

        await using var query = store.QuerySession();
        var loaded = await query.LoadAsync<RevDoc>(id);
        loaded.ShouldNotBeNull();
        loaded.Name.ShouldBe("v2");
        loaded.Version.ShouldBe(2);
    }

    [Fact]
    public async Task explicit_update_revision_higher_than_current_succeeds()
    {
        var store = RevisionedStore();

        var id = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Store(new RevDoc { Id = id, Name = "v1" });
            await session.SaveChangesAsync();
        }

        await using (var session = store.LightweightSession())
        {
            var doc = new RevDoc { Id = id, Name = "v5" };
            session.UpdateRevision(doc, 5);
            await session.SaveChangesAsync();
            doc.Version.ShouldBe(5);
        }

        await using var query = store.QuerySession();
        (await query.LoadAsync<RevDoc>(id))!.Version.ShouldBe(5);
    }

    [Fact]
    public async Task explicit_update_revision_lower_or_equal_throws()
    {
        var store = RevisionedStore();

        var id = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Store(new RevDoc { Id = id, Name = "v1" });
            session.Store(new RevDoc { Id = id, Name = "v2" });
            await session.SaveChangesAsync();
        }

        // Current is 2; supplying 2 (equal) should fail.
        await using (var session = store.LightweightSession())
        {
            session.UpdateRevision(new RevDoc { Id = id, Name = "v2-rewrite" }, 2);
            await Should.ThrowAsync<ConcurrencyException>(() => session.SaveChangesAsync());
        }

        // Supplying 1 (lower) should also fail.
        await using (var session = store.LightweightSession())
        {
            session.UpdateRevision(new RevDoc { Id = id, Name = "v1-rewrite" }, 1);
            await Should.ThrowAsync<ConcurrencyException>(() => session.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task try_update_revision_silently_skips_when_violating()
    {
        var store = RevisionedStore();

        var id = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Store(new RevDoc { Id = id, Name = "v1" });
            session.Store(new RevDoc { Id = id, Name = "v2" });
            await session.SaveChangesAsync();
        }

        // TryUpdateRevision with stale revision is a no-op — current
        // value (2) is preserved.
        await using (var session = store.LightweightSession())
        {
            session.TryUpdateRevision(new RevDoc { Id = id, Name = "ignored" }, 1);
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        var loaded = await query.LoadAsync<RevDoc>(id);
        loaded.ShouldNotBeNull();
        loaded.Name.ShouldBe("v2");
        loaded.Version.ShouldBe(2);
    }

    [Fact]
    public async Task revision_member_is_populated_on_load()
    {
        var store = RevisionedStore();

        var id = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Store(new RevDoc { Id = id, Name = "v1" });
            session.Store(new RevDoc { Id = id, Name = "v2" });
            session.Store(new RevDoc { Id = id, Name = "v3" });
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        var loaded = await query.LoadAsync<RevDoc>(id);
        loaded.ShouldNotBeNull();
        loaded.Version.ShouldBe(3);
    }

    [Fact]
    public async Task insert_collision_throws_DocumentAlreadyExists_under_numeric()
    {
        var store = RevisionedStore();

        var id = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Insert(new RevDoc { Id = id, Name = "first" });
            await session.SaveChangesAsync();
        }

        await using (var session = store.LightweightSession())
        {
            session.Insert(new RevDoc { Id = id, Name = "second" });
            await Should.ThrowAsync<DocumentAlreadyExistsException>(() => session.SaveChangesAsync());
        }
    }
}

public class RevDoc: Marten.Metadata.IRevisioned
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long Version { get; set; }
}
