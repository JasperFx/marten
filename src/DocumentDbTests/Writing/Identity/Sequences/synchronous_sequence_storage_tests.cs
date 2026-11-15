using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using Marten;
using Marten.Schema.Identity.Sequences;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace DocumentDbTests.Writing.Identity.Sequences;

public class SyncStorageDoc
{
    public int Id { get; set; }
}

/// <summary>
///     #5969: the HiLo sequence storage is built with synchronous database calls, one statement per command, so
///     that a synchronous Store() never waits on the thread pool. These hold that path to the same answers as
///     Weasel's asynchronous migration, which it replaces only for this one feature.
/// </summary>
public class synchronous_sequence_storage_tests: IAsyncLifetime
{
    private readonly string _schema = "hilo_sync_" + Guid.NewGuid().ToString("N")[..10];
    private DocumentStore _store;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_store != null)
        {
            await _store.DisposeAsync();
        }

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"drop schema if exists {_schema} cascade";
        await cmd.ExecuteNonQueryAsync();
    }

    private MartenDatabase database(AutoCreate autoCreate = AutoCreate.CreateOrUpdate)
    {
        _store ??= DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = _schema;
            opts.AutoCreateSchemaObjects = autoCreate;
            opts.Schema.For<SyncStorageDoc>();
        });

        return (MartenDatabase)_store.Storage.Database;
    }

    private SequenceFactory feature(MartenDatabase db) => new(_store.Options, db);

    private async Task<SchemaMigration> weaselMigrationAsync(MartenDatabase db)
    {
        await using var conn = db.CreateConnection();
        await conn.OpenAsync();
        return await SchemaMigration.DetermineAsync(conn, db.Migrator, CancellationToken.None, feature(db).Objects);
    }

    private SchemaMigration synchronousMigration(MartenDatabase db)
    {
        using var conn = db.CreateConnection();
        conn.Open();
        return SynchronousFeatureMigration.Determine(conn, db.Migrator, feature(db).Objects);
    }

    private static void shouldAgree(SchemaMigration synchronous, SchemaMigration weasel)
    {
        synchronous.Deltas.Select(x => (x.SchemaObject.Identifier.QualifiedName, x.Difference))
            .ShouldBe(weasel.Deltas.Select(x => (x.SchemaObject.Identifier.QualifiedName, x.Difference)));
        synchronous.Difference.ShouldBe(weasel.Difference);
    }

    private async Task executeAsync(MartenDatabase db, string sql)
    {
        await using var conn = db.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task installAnOutdatedFunctionAsync(MartenDatabase db)
    {
        // A different body under the same signature, as a database last migrated by another Marten version has
        await executeAsync(db, $@"
create or replace function {_schema}.mt_get_next_hi(entity varchar) returns bigint as
$$
declare
    current_value bigint;
begin
    select hi_value into current_value from {_schema}.mt_hilo where entity_name = entity;
    return coalesce(current_value, 0);
end
$$ language plpgsql;");
    }

    [Fact]
    public async Task reads_a_cold_schema_the_way_weasel_does()
    {
        var db = database();
        var before = SynchronousFeatureMigration.AsynchronousDeltaReads;

        var synchronous = synchronousMigration(db);
        shouldAgree(synchronous, await weaselMigrationAsync(db));
        synchronous.Difference.ShouldBe(SchemaPatchDifference.Create);

        // Every delta was read without waiting on anything
        SynchronousFeatureMigration.AsynchronousDeltaReads.ShouldBe(before);
    }

    [Fact]
    public async Task builds_the_storage_from_cold_so_weasel_sees_nothing_left_to_do()
    {
        var db = database();

        SynchronousFeatureMigration.Apply(db.CreateConnection, db.Migrator, AutoCreate.CreateOrUpdate,
            db.MigrationLogger, feature(db));

        var weasel = await weaselMigrationAsync(db);
        weasel.Difference.ShouldBe(SchemaPatchDifference.None);
        shouldAgree(synchronousMigration(db), weasel);
    }

    [Fact]
    public async Task the_first_id_builds_the_storage_and_works()
    {
        var db = database();

        await using (var session = _store.LightweightSession())
        {
            var doc = new SyncStorageDoc();
            session.Store(doc);
            doc.Id.ShouldBeGreaterThan(0);
            await session.SaveChangesAsync();
        }

        (await weaselMigrationAsync(db)).Difference.ShouldBe(SchemaPatchDifference.None);
    }

    [Fact]
    public async Task updates_an_outdated_mt_get_next_hi()
    {
        var db = database();
        SynchronousFeatureMigration.Apply(db.CreateConnection, db.Migrator, AutoCreate.CreateOrUpdate,
            db.MigrationLogger, feature(db));
        await installAnOutdatedFunctionAsync(db);

        var weasel = await weaselMigrationAsync(db);
        weasel.Difference.ShouldBe(SchemaPatchDifference.Update);
        shouldAgree(synchronousMigration(db), weasel);

        SynchronousFeatureMigration.Apply(db.CreateConnection, db.Migrator, AutoCreate.CreateOrUpdate,
            db.MigrationLogger, feature(db));

        (await weaselMigrationAsync(db)).Difference.ShouldBe(SchemaPatchDifference.None);
    }

    [Fact]
    public async Task create_only_refuses_to_update_the_function_as_weasel_would()
    {
        var db = database(AutoCreate.CreateOnly);
        SynchronousFeatureMigration.Apply(db.CreateConnection, db.Migrator, AutoCreate.CreateOnly,
            db.MigrationLogger, feature(db));
        await installAnOutdatedFunctionAsync(db);

        Should.Throw<SchemaMigrationException>(() =>
            SynchronousFeatureMigration.Apply(db.CreateConnection, db.Migrator, AutoCreate.CreateOnly,
                db.MigrationLogger, feature(db)));

        (await weaselMigrationAsync(db)).Difference.ShouldBe(SchemaPatchDifference.Update);
    }

    [Fact]
    public async Task auto_create_none_builds_nothing()
    {
        var db = database(AutoCreate.None);

        // Reading the sequences used to read the catalog and apply nothing; now it skips the database entirely
        db.Sequences.ShouldNotBeNull();

        (await weaselMigrationAsync(db)).Difference.ShouldBe(SchemaPatchDifference.Create);
    }

    [Theory]
    [InlineData("select 1; select 2", new[] { "select 1", "select 2" })]
    [InlineData("select 1;;  ;\n select 2;", new[] { "select 1", "select 2" })]
    [InlineData("select 'a;b'; select 2", new[] { "select 'a;b'", "select 2" })]
    [InlineData("select 'it''s;'; select 2", new[] { "select 'it''s;'", "select 2" })]
    [InlineData("select E'\\';'; select 2", new[] { "select E'\\';'", "select 2" })]
    [InlineData("select \"a;b\" from t; select 2", new[] { "select \"a;b\" from t", "select 2" })]
    [InlineData("do $$ begin perform 1; end $$; select 2", new[] { "do $$ begin perform 1; end $$", "select 2" })]
    [InlineData("create function f() returns int as $body$ select 1; $body$ language sql; select 2",
        new[] { "create function f() returns int as $body$ select 1; $body$ language sql", "select 2" })]
    [InlineData("select $1; select 2", new[] { "select $1", "select 2" })]
    [InlineData("-- a; comment\nselect 1; /* b; /* nested; */ c; */ select 2",
        new[] { "-- a; comment\nselect 1", "/* b; /* nested; */ c; */ select 2" })]
    [InlineData("-- only a comment;\n; ", new string[0])]
    public void splits_statements_only_at_the_semicolons_that_end_them(string sql, string[] expected)
    {
        SynchronousFeatureMigration.SplitStatements(sql).ShouldBe(expected);
    }
}
