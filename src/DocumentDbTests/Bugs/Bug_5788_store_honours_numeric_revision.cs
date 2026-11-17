using System;
using System.Threading.Tasks;
using JasperFx;
using Marten;
using Marten.Schema;
using Marten.Services;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Bugs;

/// <summary>
///     #5788 (jasperfx#785). <c>Store()</c> on a numerically revisioned document must honour the
///     document's own revision: a non-zero value is the revision the caller asks the document to
///     BECOME, accepted only when strictly greater than the stored one and landing at exactly that
///     value; <c>0</c> is the auto sentinel. Before the fix the revision only reached the identity map,
///     the upsert went out at the auto sentinel, and a stale writer silently overwrote a newer row.
/// </summary>
/// <remarks>
///     The rows A-E are the issue's table. Every stored revision is read from the revision COLUMN with
///     raw SQL, never off a loaded document: a store that ignored revisions altogether would still
///     round-trip the member as an ordinary JSON property, so a loaded document cannot tell "stored at
///     10" from "a document whose Version field says 10" (the vacuous-read hazard on jasperfx#785).
/// </remarks>
public class Bug_5788_store_honours_numeric_revision: OneOffConfigurationsContext
{
    public Bug_5788_store_honours_numeric_revision()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<RevDoc5788>();
            opts.Schema.For<LongRevDoc5788>();
            opts.Schema.For<MappedRevDoc5788>().UseNumericRevisions(true)
                .Metadata(m => m.Revision.MapTo(x => x.Revision));
        });
    }

    private IDocumentSession openSession(DocumentTracking tracking)
    {
        var session = theStore.OpenSession(new SessionOptions { Tracking = tracking });
        Disposables.Add(session);
        return session;
    }

    private async Task<long> storedRevisionAsync<T>(Guid id)
    {
        var table = theStore.Options.Storage.MappingFor(typeof(T)).TableName.QualifiedName;

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"select mt_version from {table} where id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);

        var raw = await cmd.ExecuteScalarAsync();
        raw.ShouldNotBeNull($"no row for {id}");
        return Convert.ToInt64(raw);
    }

    private async Task<string> storedValueAsync(Guid id)
    {
        await using var query = theStore.QuerySession();
        return (await query.LoadAsync<RevDoc5788>(id))!.Value;
    }

    private async Task<Guid> seedAsync()
    {
        var id = Guid.NewGuid();
        await using var session = theStore.LightweightSession();
        session.Store(new RevDoc5788 { Id = id, Value = "original" });
        await session.SaveChangesAsync();

        (await storedRevisionAsync<RevDoc5788>(id)).ShouldBe(1);
        return id;
    }

    [Theory]
    [SessionTypes]
    public async Task A_restoring_a_loaded_document_at_its_loaded_revision_is_refused(DocumentTracking tracking)
    {
        var id = await seedAsync();

        var session = openSession(tracking);
        var doc = await session.LoadAsync<RevDoc5788>(id);
        doc!.Version.ShouldBe(1);
        doc.Value = "changed";
        session.Store(doc);

        // 1 is not greater than 1
        await Should.ThrowAsync<ConcurrencyException>(() => session.SaveChangesAsync());

        (await storedRevisionAsync<RevDoc5788>(id)).ShouldBe(1);
        (await storedValueAsync(id)).ShouldBe("original");
    }

    [Theory]
    [SessionTypes]
    public async Task B_a_stale_second_writer_is_refused(DocumentTracking tracking)
    {
        var id = await seedAsync();

        var session1 = openSession(tracking);
        var session2 = openSession(tracking);

        var doc1 = await session1.LoadAsync<RevDoc5788>(id);
        var doc2 = await session2.LoadAsync<RevDoc5788>(id);
        doc1!.Version.ShouldBe(1);
        doc2!.Version.ShouldBe(1);

        doc1.Value = "first writer";
        doc1.Version = 2;
        session1.Store(doc1);
        await session1.SaveChangesAsync();
        (await storedRevisionAsync<RevDoc5788>(id)).ShouldBe(2);

        // Still names revision 2, but the row is already at 2: stale
        doc2.Value = "stale writer";
        doc2.Version = 2;
        session2.Store(doc2);
        await Should.ThrowAsync<ConcurrencyException>(() => session2.SaveChangesAsync());

        (await storedRevisionAsync<RevDoc5788>(id)).ShouldBe(2);
        (await storedValueAsync(id)).ShouldBe("first writer");
    }

    [Theory]
    [SessionTypes]
    public async Task C_an_explicit_revision_lands_at_exactly_that_value(DocumentTracking tracking)
    {
        var id = await seedAsync();

        var session = openSession(tracking);
        var doc = await session.LoadAsync<RevDoc5788>(id);
        doc!.Value = "jumped";
        doc.Version = 10;
        session.Store(doc);
        await session.SaveChangesAsync();

        (await storedRevisionAsync<RevDoc5788>(id)).ShouldBe(10);
        (await storedValueAsync(id)).ShouldBe("jumped");
    }

    [Theory]
    [SessionTypes]
    public async Task D_update_revision_is_unchanged(DocumentTracking tracking)
    {
        var id = await seedAsync();

        var session = openSession(tracking);
        var doc = await session.LoadAsync<RevDoc5788>(id);
        doc!.Value = "via UpdateRevision";
        session.UpdateRevision(doc, 2);
        await session.SaveChangesAsync();

        (await storedRevisionAsync<RevDoc5788>(id)).ShouldBe(2);

        var again = openSession(tracking);
        var doc2 = await again.LoadAsync<RevDoc5788>(id);
        doc2!.Value = "again at 2";
        again.UpdateRevision(doc2, 2);
        await Should.ThrowAsync<ConcurrencyException>(() => again.SaveChangesAsync());

        (await storedRevisionAsync<RevDoc5788>(id)).ShouldBe(2);
        (await storedValueAsync(id)).ShouldBe("via UpdateRevision");
    }

    [Theory]
    [SessionTypes]
    public async Task E_a_zero_revision_is_the_auto_sentinel(DocumentTracking tracking)
    {
        var id = await seedAsync();

        var session = openSession(tracking);
        var doc = await session.LoadAsync<RevDoc5788>(id);
        doc!.Value = "auto";
        doc.Version = 0;
        session.Store(doc);
        await session.SaveChangesAsync();

        (await storedRevisionAsync<RevDoc5788>(id)).ShouldBe(2);
        (await storedValueAsync(id)).ShouldBe("auto");
    }

    [Fact]
    public async Task an_explicit_revision_on_a_new_document_is_honoured()
    {
        var id = Guid.NewGuid();
        await using var session = theStore.LightweightSession();
        session.Store(new RevDoc5788 { Id = id, Value = "imported", Version = 7 });
        await session.SaveChangesAsync();

        (await storedRevisionAsync<RevDoc5788>(id)).ShouldBe(7);
    }

    [Fact]
    public async Task store_objects_follows_the_same_rule()
    {
        var id = await seedAsync();

        await using var session = theStore.LightweightSession();
        session.StoreObjects([new RevDoc5788 { Id = id, Value = "stale", Version = 1 }]);
        await Should.ThrowAsync<ConcurrencyException>(() => session.SaveChangesAsync());

        (await storedRevisionAsync<RevDoc5788>(id)).ShouldBe(1);
    }

    [Fact]
    public async Task concurrency_checks_disabled_still_overwrites()
    {
        var id = await seedAsync();

        await using var session = theStore.OpenSession(new SessionOptions
        {
            ConcurrencyChecks = ConcurrencyChecks.Disabled
        });
        session.Store(new RevDoc5788 { Id = id, Value = "forced", Version = 1 });
        await session.SaveChangesAsync();

        // ConcurrencyChecks.Disabled is the documented escape hatch: an overwrite, no guard
        (await storedValueAsync(id)).ShouldBe("forced");
        (await storedRevisionAsync<RevDoc5788>(id)).ShouldBe(2);
    }

    [Theory]
    [SessionTypes]
    public async Task long_versioned_documents_follow_the_same_rule(DocumentTracking tracking)
    {
        var id = Guid.NewGuid();
        await using (var seed = theStore.LightweightSession())
        {
            seed.Store(new LongRevDoc5788 { Id = id, Value = "original" });
            await seed.SaveChangesAsync();
        }

        (await storedRevisionAsync<LongRevDoc5788>(id)).ShouldBe(1);

        // Re-store at the loaded revision: refused
        var stale = openSession(tracking);
        var loaded = await stale.LoadAsync<LongRevDoc5788>(id);
        loaded!.Version.ShouldBe(1);
        loaded.Value = "stale";
        stale.Store(loaded);
        await Should.ThrowAsync<ConcurrencyException>(() => stale.SaveChangesAsync());
        (await storedRevisionAsync<LongRevDoc5788>(id)).ShouldBe(1);

        // Jump past Int32.MaxValue: lands exactly there
        var big = (long)int.MaxValue + 5;
        var jump = openSession(tracking);
        var doc = await jump.LoadAsync<LongRevDoc5788>(id);
        doc!.Value = "big";
        doc.Version = big;
        jump.Store(doc);
        await jump.SaveChangesAsync();
        (await storedRevisionAsync<LongRevDoc5788>(id)).ShouldBe(big);

        // 0 is auto
        var auto = openSession(tracking);
        var doc2 = await auto.LoadAsync<LongRevDoc5788>(id);
        doc2!.Version = 0;
        auto.Store(doc2);
        await auto.SaveChangesAsync();
        (await storedRevisionAsync<LongRevDoc5788>(id)).ShouldBe(big + 1);
    }

    [Fact]
    public async Task a_mapped_revision_member_follows_the_same_rule()
    {
        var id = Guid.NewGuid();
        await using (var seed = theStore.LightweightSession())
        {
            seed.Store(new MappedRevDoc5788 { Id = id, Value = "original" });
            await seed.SaveChangesAsync();
        }

        (await storedRevisionAsync<MappedRevDoc5788>(id)).ShouldBe(1);

        await using (var stale = theStore.LightweightSession())
        {
            stale.Store(new MappedRevDoc5788 { Id = id, Value = "stale", Revision = 1 });
            await Should.ThrowAsync<ConcurrencyException>(() => stale.SaveChangesAsync());
        }

        await using (var jump = theStore.LightweightSession())
        {
            jump.Store(new MappedRevDoc5788 { Id = id, Value = "jumped", Revision = 4 });
            await jump.SaveChangesAsync();
        }

        (await storedRevisionAsync<MappedRevDoc5788>(id)).ShouldBe(4);
    }
}

public class RevDoc5788: IRevisioned
{
    public Guid Id { get; set; }
    public string Value { get; set; } = "";
    public int Version { get; set; }
}

public class LongRevDoc5788: ILongVersioned
{
    public Guid Id { get; set; }
    public string Value { get; set; } = "";
    public long Version { get; set; }
}

public class MappedRevDoc5788
{
    public Guid Id { get; set; }
    public string Value { get; set; } = "";
    public long Revision { get; set; }
}
