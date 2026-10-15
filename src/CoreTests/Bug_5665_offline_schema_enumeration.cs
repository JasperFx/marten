using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using Marten;
using Marten.Schema.Identity.Sequences;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using Xunit;

namespace CoreTests;

/// <summary>
///     https://github.com/JasperFx/marten/issues/5665.
/// </summary>
/// <remarks>
///     <para>
///     <c>MartenDatabase.Sequences</c> is a <see cref="Lazy{T}" /> whose factory applied the
///     <c>mt_hilo</c> / <c>mt_get_next_hi</c> DDL before handing the <see cref="SequenceFactory" />
///     back. That eager apply belongs to the runtime path that hands out Hi-Lo ids — it is what
///     lets an id assignment assume the table is there. But <c>StorageFeatures.AllActiveFeatures</c>
///     yielded that same property whenever any document had a numeric id, so the first read of it
///     came from <c>AllObjects()</c>: merely *enumerating* the schema opened a connection and ran DDL.
///     </para>
///     <para>
///     That made <c>db-dump</c> — which users and their DBAs run on a build machine with no database
///     in reach — connect and provision, the moment one <c>int</c>-id document was registered.
///     </para>
///     <para>
///     The unreachable-host tests below are the real regression guard: nothing listens on port 1, so
///     any connection attempt fails fast and loudly instead of passing silently against whatever
///     database happens to be around.
///     </para>
/// </remarks>
public class Bug_5665_offline_schema_enumeration
{
    // Nothing listens on port 1, so any connection attempt fails immediately rather than hanging.
    private const string Unreachable =
        "Host=127.0.0.1;Port=1;Database=nope;Username=nobody;Password=nothing;Timeout=2;Command Timeout=2";

    private static IMartenDatabase offlineDatabase(Action<StoreOptions> configure)
    {
        var store = DocumentStore.For(opts =>
        {
            opts.Connection(Unreachable);
            configure(opts);
        });

        return store.Storage.Database;
    }

    private static async Task<Table?> existingHiloTable(string schemaName)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        return await new Table(new PostgresqlObjectName(schemaName, "mt_hilo")).FetchExistingAsync(conn);
    }

    [Fact]
    public void AllObjects_does_not_connect_for_a_numeric_id_document()
    {
        var database = offlineDatabase(opts => opts.Schema.For<IntIdDoc>());

        var objects = database.AllObjects().ToArray();

        objects.ShouldNotBeEmpty();
    }

    [Fact]
    public void AllObjects_does_not_connect_for_a_long_id_document()
    {
        var database = offlineDatabase(opts => opts.Schema.For<LongIdDoc>());

        database.AllObjects().ShouldNotBeEmpty();
    }

    [Fact]
    public void AllObjects_still_does_not_connect_for_a_guid_id_document()
    {
        // The control: this worked before the fix and must keep working.
        var database = offlineDatabase(opts => opts.Schema.For<GuidIdDoc>());

        database.AllObjects().ShouldNotBeEmpty();
    }

    [Fact]
    public void the_hilo_objects_are_still_listed_so_the_dump_is_complete()
    {
        // Side-effect free is only half of it. db-dump exists to write a COMPLETE script, so the
        // Hi-Lo table and function have to show up in the listing all the same.
        var database = offlineDatabase(opts => opts.Schema.For<IntIdDoc>());

        var names = database.AllObjects()
            .SelectMany(x => x.AllNames())
            .Select(x => x.Name)
            .ToArray();

        names.ShouldContain("mt_hilo");
        names.ShouldContain("mt_get_next_hi");
    }

    [Fact]
    public void the_hilo_objects_are_absent_when_no_document_needs_a_sequence()
    {
        var database = offlineDatabase(opts => opts.Schema.For<GuidIdDoc>());

        database.AllObjects()
            .SelectMany(x => x.AllNames())
            .Select(x => x.Name)
            .ShouldNotContain("mt_hilo");
    }

    [Fact]
    public void AllSchemaNames_does_not_connect_for_a_numeric_id_document()
    {
        var database = offlineDatabase(opts => opts.Schema.For<IntIdDoc>());

        database.AllSchemaNames().ShouldContain("public");
    }

    [Fact]
    public void ToDatabaseScript_does_not_connect_for_a_numeric_id_document()
    {
        // This is what db-dump writes out.
        var database = offlineDatabase(opts => opts.Schema.For<IntIdDoc>());

        var script = database.ToDatabaseScript();

        script.ShouldContain("mt_hilo");
        script.ShouldContain("mt_get_next_hi");
    }

    [Fact]
    public void BuildFeatureSchemas_does_not_connect_for_a_numeric_id_document()
    {
        var database = offlineDatabase(opts => opts.Schema.For<IntIdDoc>());

        database.BuildFeatureSchemas()
            .ShouldContain(x => x.StorageType == typeof(SequenceFactory));
    }

    [Fact]
    public void enumerating_the_schema_does_not_connect_even_with_AutoCreate_None()
    {
        // AutoCreate.None is how a team that reviews its own SQL configures the store, and it is
        // the configuration most likely to be pointed at a database it cannot reach.
        var database = offlineDatabase(opts =>
        {
            opts.AutoCreateSchemaObjects = AutoCreate.None;
            opts.Schema.For<IntIdDoc>();
        });

        database.AllObjects().ShouldNotBeEmpty();
    }

    [Fact]
    public async Task enumerating_against_a_reachable_database_provisions_nothing()
    {
        // The other half of the symptom: with a database actually in reach, listing the objects ran
        // the CREATE TABLE / CREATE FUNCTION rather than failing — so the damage was silent.
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "bug_5665_listing";
            opts.Schema.For<IntIdDoc>();
        });

        await store.Advanced.Clean.CompletelyRemoveAllAsync();

        store.Storage.Database.AllObjects().ToArray();
        store.Storage.Database.ToDatabaseScript();

        (await existingHiloTable("bug_5665_listing"))
            .ShouldBeNull("Enumerating the schema must not have created mt_hilo");
    }

    [Fact]
    public async Task an_int_id_document_still_round_trips_after_a_full_apply()
    {
        // The ordering most exposed by the fix. Reading `Sequences` used to register Weasel's
        // "already checked" entry for the Hi-Lo feature as a side effect of a full apply walking the
        // feature list; now it does not, so the first id assignment has to stand on its own.
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "bug_5665_apply";
            opts.Schema.For<IntIdDoc>();
        });

        await store.Advanced.Clean.CompletelyRemoveAllAsync();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        (await existingHiloTable("bug_5665_apply"))
            .ShouldNotBeNull("A full apply must still create mt_hilo");

        await using var session = store.LightweightSession();
        var doc = new IntIdDoc { Name = "Bessie" };
        session.Store(doc);
        await session.SaveChangesAsync();

        doc.Id.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task the_runtime_hilo_path_still_provisions_its_own_storage()
    {
        // The guarantee the eager apply existed for: asking for a sequence makes the table real,
        // with no separate schema-management call in between.
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "bug_5665";
            opts.Schema.For<IntIdDoc>();
        });

        await store.Advanced.Clean.CompletelyRemoveAllAsync();

        store.Storage.Database.Sequences.SequenceFor(typeof(IntIdDoc)).ShouldNotBeNull();

        (await existingHiloTable("bug_5665"))
            .ShouldNotBeNull("Reading Sequences must still have created mt_hilo");
    }

    [Fact]
    public async Task an_int_id_document_still_round_trips()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "bug_5665_roundtrip";
            opts.Schema.For<IntIdDoc>();
        });

        await store.Advanced.Clean.CompletelyRemoveAllAsync();

        await using var session = store.LightweightSession();
        var doc = new IntIdDoc { Name = "Hank" };
        session.Store(doc);
        await session.SaveChangesAsync();

        doc.Id.ShouldBeGreaterThan(0);

        await using var query = store.QuerySession();
        (await query.LoadAsync<IntIdDoc>(doc.Id)).Name.ShouldBe("Hank");
    }

    public class IntIdDoc
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class LongIdDoc
    {
        public long Id { get; set; }
    }

    public class GuidIdDoc
    {
        public Guid Id { get; set; }
    }
}
