using System;
using System.Threading.Tasks;
using JasperFx;
using Marten;
using Marten.Exceptions;
using Marten.Schema;
using Marten.Services;
using Marten.Internal.ClosedShape;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests.Storage.Identification;

/// <summary>
/// W3 spike M7: validates optimistic-concurrency behavior on the
/// closed-shape document storage. UseOptimisticConcurrency on the
/// mapping turns on Guid-version WHERE filters + version writeback.
/// Mismatched expected versions raise <see cref="ConcurrencyException"/>;
/// <c>session.Store(doc, ignoreConcurrencyCheck: true)</c> bypasses the
/// check via the Overwrite operation.
/// </summary>
public class closed_shape_optimistic_concurrency_tests: BugIntegrationContext
{
    private DocumentStore OptimisticStore()
        => StoreOptions(opts =>
        {
            opts.UseClosedShapeDocumentStorage = true;
            opts.Schema.For<OcDoc>().UseOptimisticConcurrency(true);
        });

    [Fact]
    public async Task insert_then_load_then_update_round_trips_the_version()
    {
        var store = OptimisticStore();

        var id = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Store(new OcDoc { Id = id, Name = "v1" });
            await session.SaveChangesAsync();
        }

        // Load populates session.Versions; subsequent Store works.
        await using (var session = store.LightweightSession())
        {
            var doc = await session.LoadAsync<OcDoc>(id);
            doc.ShouldNotBeNull();
            doc.Name = "v2";
            session.Store(doc);
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        var loaded = await query.LoadAsync<OcDoc>(id);
        loaded.ShouldNotBeNull();
        loaded.Name.ShouldBe("v2");
    }

    [Fact]
    public async Task concurrent_update_from_stale_version_throws()
    {
        var store = OptimisticStore();

        var id = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Store(new OcDoc { Id = id, Name = "v1" });
            await session.SaveChangesAsync();
        }

        // Session A loads — captures v1 version. Session B then loads and
        // writes v2, advancing the row's version. Session A still has v1
        // — its save attempt must raise ConcurrencyException.
        await using var sessionA = store.LightweightSession();
        var docA = await sessionA.LoadAsync<OcDoc>(id);
        docA.ShouldNotBeNull();

        await using (var sessionB = store.LightweightSession())
        {
            var docB = await sessionB.LoadAsync<OcDoc>(id);
            docB.ShouldNotBeNull();
            docB.Name = "from-B";
            sessionB.Store(docB);
            await sessionB.SaveChangesAsync();
        }

        docA.Name = "from-A-stale";
        sessionA.Store(docA);
        await Should.ThrowAsync<ConcurrencyException>(() => sessionA.SaveChangesAsync());

        // B's write should still be the authoritative one in the DB.
        await using var query = store.QuerySession();
        (await query.LoadAsync<OcDoc>(id))!.Name.ShouldBe("from-B");
    }

    [Fact]
    public async Task update_without_a_prior_load_throws()
    {
        var store = OptimisticStore();

        var id = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Store(new OcDoc { Id = id, Name = "v1" });
            await session.SaveChangesAsync();
        }

        // Fresh session — session.Versions is empty — DBNull expected
        // version — WHERE filter rejects → ConcurrencyException.
        await using (var session = store.LightweightSession())
        {
            session.Store(new OcDoc { Id = id, Name = "blind-write" });
            await Should.ThrowAsync<ConcurrencyException>(() => session.SaveChangesAsync());
        }

        await using var query = store.QuerySession();
        (await query.LoadAsync<OcDoc>(id))!.Name.ShouldBe("v1");
    }

    [Fact]
    public async Task explicit_update_throws_when_id_is_missing()
    {
        var store = OptimisticStore();

        var id = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            // session.Update against a row that doesn't exist — under
            // optimistic concurrency the missing row surfaces as
            // ConcurrencyException (codegen behavior preserved).
            session.Update(new OcDoc { Id = id, Name = "never-existed" });
            await Should.ThrowAsync<ConcurrencyException>(() => session.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task insert_collision_throws_DocumentAlreadyExists_even_under_optimistic()
    {
        var store = OptimisticStore();

        var id = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Insert(new OcDoc { Id = id, Name = "first" });
            await session.SaveChangesAsync();
        }

        // Same id again — collision detection is independent of
        // concurrency mode.
        await using (var session = store.LightweightSession())
        {
            session.Insert(new OcDoc { Id = id, Name = "second" });
            await Should.ThrowAsync<DocumentAlreadyExistsException>(() => session.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task version_member_is_assigned_after_insert()
    {
        var store = OptimisticStore();

        var doc = new OcDoc { Id = Guid.NewGuid(), Name = "fresh" };
        await using var session = store.LightweightSession();
        session.Store(doc);
        await session.SaveChangesAsync();

        // The operation writes the new version back onto the document so
        // callers can read it from the same instance.
        doc.Version.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task overwrite_bypasses_the_version_check()
    {
        var store = OptimisticStore();

        var id = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Store(new OcDoc { Id = id, Name = "v1" });
            await session.SaveChangesAsync();
        }

        // Open a session with concurrency disabled — Store routes to
        // Overwrite, which uses the closed-shape OverwriteSql (no
        // mt_version = ? filter). Even without knowing the prior version
        // the write must win.
        await using (var session = store.LightweightSession(new SessionOptions { ConcurrencyChecks = ConcurrencyChecks.Disabled }))
        {
            session.Store(new OcDoc { Id = id, Name = "overwritten" });
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        (await query.LoadAsync<OcDoc>(id))!.Name.ShouldBe("overwritten");
    }

    [Fact]
    public async Task version_member_is_populated_on_load()
    {
        var store = OptimisticStore();

        var id = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Store(new OcDoc { Id = id, Name = "fresh" });
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        var loaded = await query.LoadAsync<OcDoc>(id);
        loaded.ShouldNotBeNull();
        loaded.Version.ShouldNotBe(Guid.Empty);
    }
}

public class OcDoc
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // [Version] auto-enables UseOptimisticConcurrency on the mapping AND
    // wires the version binder's setter to this member so reads / writes
    // round-trip the current row version.
    [Version]
    public Guid Version { get; set; }
}
