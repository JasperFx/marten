using System;
using System.Threading.Tasks;
using JasperFx;
using Marten;
using Marten.Metadata;
using Marten.Services;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests.Storage.Identification;

/// <summary>
/// #5372, reported by JurJean, whose reproduction and the first four tests below this project
/// adopts verbatim.
///
/// The documented contract for a mapped version member —
/// Metadata(m => m.Version.MapTo(x => x.Etag)) on a UseOptimisticConcurrency(true) document
/// (docs/documents/metadata.md) — is that Store() carries the version number on the document
/// itself, i.e. the mapped member behaves exactly like the IVersioned marker interface.
///
/// It did not. storeEntity() seeded the session's expected version only from the marker
/// interfaces, so a mapped member was invisible: the upsert bound DBNull into its
/// ON CONFLICT ... WHERE mt_version = ? guard, no RETURNING row came back, and every
/// cross-session write of such a document was reported as a ConcurrencyException.
/// </summary>
public class closed_shape_optimistic_concurrency_mapped_member_tests: BugIntegrationContext
{
    private DocumentStore mappedMemberStore()
        => StoreOptions(opts =>
        {
            opts.Schema.For<MappedVersionDoc>().UseOptimisticConcurrency(true)
                .Metadata(m => m.Version.MapTo(x => x.Etag));
        });

    private DocumentStore markerInterfaceStore()
        => StoreOptions(opts =>
        {
            opts.Schema.For<MarkerVersionedDoc>().UseOptimisticConcurrency(true);
        });

    [Fact]
    public async Task mapped_version_member_should_be_used_as_the_expected_version_on_store()
    {
        var store = mappedMemberStore();

        var doc = new MappedVersionDoc { Name = "v1" };
        await using (var session = store.LightweightSession())
        {
            session.Store(doc);
            await session.SaveChangesAsync();
        }

        // The mapped member received the version Marten wrote
        doc.Etag.ShouldNotBe(Guid.Empty);

        // Re-storing the SAME entity in a fresh session must not be a blind
        // write: the mapped member carries the expected version.
        await using (var session = store.LightweightSession())
        {
            doc.Name = "v2";
            session.Store(doc);
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        (await query.LoadAsync<MappedVersionDoc>(doc.Id))!.Name.ShouldBe("v2");
    }

    [Fact]
    public async Task stale_mapped_version_member_is_rejected()
    {
        var store = mappedMemberStore();

        var doc = new MappedVersionDoc { Name = "v1" };
        await using (var session = store.LightweightSession())
        {
            session.Store(doc);
            await session.SaveChangesAsync();
        }

        await using var session2 = store.LightweightSession();
        doc.Etag = Guid.NewGuid(); // a version that never existed on the row
        session2.Store(doc);
        await Should.ThrowAsync<ConcurrencyException>(() => session2.SaveChangesAsync());
    }

    [Fact]
    public async Task mapped_version_member_of_empty_guid_is_rejected_like_a_blind_write()
    {
        var store = mappedMemberStore();

        var id = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Store(new MappedVersionDoc { Id = id, Name = "v1" });
            await session.SaveChangesAsync();
        }

        // No version on the document = no expected version = rejected, exactly
        // like the unversioned blind write case
        await using var session2 = store.LightweightSession();
        session2.Store(new MappedVersionDoc { Id = id, Name = "blind-write" });
        await Should.ThrowAsync<ConcurrencyException>(() => session2.SaveChangesAsync());

        await using var query = store.QuerySession();
        (await query.LoadAsync<MappedVersionDoc>(id))!.Name.ShouldBe("v1");
    }

    [Fact]
    public async Task marker_interface_version_round_trips_in_a_fresh_session()
    {
        // Contrast: the same flow through the IVersioned marker interface worked all along,
        // because storeEntity() seeds the session's version tracker from the marker interface.
        // MapTo() members now behave identically.
        var store = markerInterfaceStore();

        var doc = new MarkerVersionedDoc { Name = "v1" };
        await using (var session = store.LightweightSession())
        {
            session.Store(doc);
            await session.SaveChangesAsync();
        }

        doc.Version.ShouldNotBe(Guid.Empty);

        await using (var session = store.LightweightSession())
        {
            doc.Name = "v2";
            session.Store(doc);
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        (await query.LoadAsync<MarkerVersionedDoc>(doc.Id))!.Name.ShouldBe("v2");
    }

    [Fact]
    public async Task concurrency_checks_disabled_still_bypasses_the_mapped_member()
    {
        // The no-version Store(session, doc) overload is HOW ConcurrencyChecks.Disabled is
        // implemented (DocumentSessionBase takes that branch deliberately), so seeding from the
        // mapped member must not reach it. A stale version has to be ignored here.
        var store = mappedMemberStore();

        var doc = new MappedVersionDoc { Name = "v1" };
        await using (var session = store.LightweightSession())
        {
            session.Store(doc);
            await session.SaveChangesAsync();
        }

        doc.Etag = Guid.NewGuid(); // stale, and deliberately not to be enforced
        doc.Name = "forced";

        await using (var session =
                     store.LightweightSession(new SessionOptions { ConcurrencyChecks = ConcurrencyChecks.Disabled }))
        {
            session.Store(doc);
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        (await query.LoadAsync<MappedVersionDoc>(doc.Id))!.Name.ShouldBe("forced");
    }

    [Fact]
    public async Task mapped_version_member_on_a_subclass_uses_the_root_storage()
    {
        // SubClassDocumentStorage forwards to the root storage, which owns the mapping. The member
        // is declared on the root type and readable from the subclass instance.
        var store = StoreOptions(opts =>
        {
            opts.Schema.For<MappedVersionRoot>()
                .AddSubClass<MappedVersionChild>()
                .UseOptimisticConcurrency(true)
                .Metadata(m => m.Version.MapTo(x => x.Etag));
        });

        var doc = new MappedVersionChild { Name = "v1", Extra = "x" };
        await using (var session = store.LightweightSession())
        {
            session.Store(doc);
            await session.SaveChangesAsync();
        }

        doc.Etag.ShouldNotBe(Guid.Empty);

        await using (var session = store.LightweightSession())
        {
            doc.Name = "v2";
            session.Store(doc);
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        (await query.LoadAsync<MappedVersionChild>(doc.Id))!.Name.ShouldBe("v2");
    }

    [Fact]
    public async Task stale_mapped_version_member_on_a_subclass_is_rejected()
    {
        var store = StoreOptions(opts =>
        {
            opts.Schema.For<MappedVersionRoot>()
                .AddSubClass<MappedVersionChild>()
                .UseOptimisticConcurrency(true)
                .Metadata(m => m.Version.MapTo(x => x.Etag));
        });

        var doc = new MappedVersionChild { Name = "v1", Extra = "x" };
        await using (var session = store.LightweightSession())
        {
            session.Store(doc);
            await session.SaveChangesAsync();
        }

        await using var session2 = store.LightweightSession();
        doc.Etag = Guid.NewGuid();
        session2.Store(doc);
        await Should.ThrowAsync<ConcurrencyException>(() => session2.SaveChangesAsync());
    }

    [Fact]
    public async Task mapped_revision_member_round_trips_in_a_fresh_session()
    {
        // The numeric-revision sibling of the same seam. The getter is built whenever
        // Metadata.Revision.Enabled is set, which Revision.MapTo(...) does on its own.
        var store = StoreOptions(opts =>
        {
            opts.Schema.For<MappedRevisionDoc>().UseNumericRevisions(true)
                .Metadata(m => m.Revision.MapTo(x => x.Rev));
        });

        var doc = new MappedRevisionDoc { Name = "v1" };
        await using (var session = store.LightweightSession())
        {
            session.Store(doc);
            await session.SaveChangesAsync();
        }

        doc.Rev.ShouldBeGreaterThan(0);

        await using (var session = store.LightweightSession())
        {
            doc.Name = "v2";
            session.Store(doc);
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        (await query.LoadAsync<MappedRevisionDoc>(doc.Id))!.Name.ShouldBe("v2");
    }
}

public class MappedVersionDoc
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public Guid Etag { get; set; }
}

public class MarkerVersionedDoc: IVersioned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public Guid Version { get; set; }
}

public class MappedVersionRoot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public Guid Etag { get; set; }
}

public class MappedVersionChild: MappedVersionRoot
{
    public string Extra { get; set; } = "";
}

public class MappedRevisionDoc
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public int Rev { get; set; }
}
