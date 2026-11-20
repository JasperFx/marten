#nullable enable
using System;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Weasel.Postgresql.Migrations;
using Xunit;

namespace EventSourcingTests.Dcb;

public record Bug5898Student(Guid Value);
public record Bug5898Enrolled(string Name);

/// <summary>
/// #5898: <c>DcbStorageMode = HStore</c> calls <c>ConfigureNpgsqlDataSourceBuilder</c>, which replaced the data
/// source factory -- so a data source supplied earlier through <c>opts.Connection(NpgsqlDataSource)</c> was
/// dropped, and Marten built a second one from its <c>ConnectionString</c>. Npgsql strips the password from
/// that string, so the first connection failed with "No password has been provided".
/// </summary>
// Builds its own stores: the harness's StoreOptions() connects by connection string first, and a store that
// already has a connection does not take a later Connection(dataSource) as its primary database.
[Collection("OneOffs")]
public class Bug_5898_hstore_keeps_a_supplied_data_source: IAsyncLifetime
{
    // Built the ordinary way: its ConnectionString no longer carries the password
    private readonly NpgsqlDataSource _supplied = NpgsqlDataSource.Create(ConnectionSource.ConnectionString);
    private DocumentStore? _store;

    private DocumentStore theStore => _store!;

    private void StoreOptions(Action<StoreOptions> configure)
    {
        _store = DocumentStore.For(opts =>
        {
            opts.DatabaseSchemaName = "bug_5898";
            configure(opts);
        });
    }

    private static void HStore(StoreOptions opts)
    {
        opts.Events.DcbStorageMode = DcbStorageMode.HStore;
        opts.Events.AddEventType<Bug5898Enrolled>();
        opts.Events.RegisterTagType<Bug5898Student>("bug5898_student");
    }

    [Fact]
    public async Task hstore_mode_after_the_data_source()
    {
        StoreOptions(opts =>
        {
            opts.Connection(_supplied);
            HStore(opts);
        });

        await assertTheStoreRunsOnTheSuppliedDataSource();
    }

    [Fact]
    public async Task hstore_mode_before_the_data_source()
    {
        // The order UseNpgsqlDataSource() produces: it applies Connection(...) after the AddMarten lambda
        StoreOptions(opts =>
        {
            HStore(opts);
            opts.Connection(_supplied);
        });

        await assertTheStoreRunsOnTheSuppliedDataSource();
    }

    [Fact]
    public async Task any_builder_configuration_after_the_data_source()
    {
        StoreOptions(opts =>
        {
            opts.Connection(_supplied);
            opts.ConfigureNpgsqlDataSourceBuilder(_ => { });
            opts.Events.AddEventType<Bug5898Enrolled>();
            opts.Events.RegisterTagType<Bug5898Student>("bug5898_student");
        });

        await assertTheStoreRunsOnTheSuppliedDataSource();
    }

    /// <summary>
    ///     The tests above only failed when they were the first to create the hstore extension in their database:
    ///     the supplied data source loaded its types on the migration's own connection, before the extension
    ///     existed, and nothing reloaded them. Dropping the extension from a database of this test's own makes that
    ///     the case every time.
    /// </summary>
    /// <remarks>
    ///     The database is created once and kept, named after this run's database so that lanes and checkouts do
    ///     not share it. Creating and dropping a database on every run would not do: DROP DATABASE forces an
    ///     immediate checkpoint, which stalls every other lane on the server for long enough to time out.
    /// </remarks>
    [Fact]
    public async Task hstore_mode_on_a_database_without_the_extension_yet()
    {
        var baseName = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString).Database!;
        var databaseName = $"{baseName[..Math.Min(baseName.Length, 50)]}_b5898";

        string connectionString;
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            if (!await conn.DatabaseExists(databaseName))
            {
                try
                {
                    await new DatabaseSpecification().BuildDatabase(conn, databaseName);
                }
                catch (PostgresException e) when (e.SqlState is PostgresErrorCodes.DuplicateDatabase
                                                      or PostgresErrorCodes.UniqueViolation)
                {
                    // Another run got there first
                }
            }

            connectionString = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString)
            {
                Database = databaseName
            }.ConnectionString;
        }

        await using (var conn = new NpgsqlConnection(connectionString))
        {
            await conn.OpenAsync();
            await conn.CreateCommand("drop schema if exists bug_5898 cascade").ExecuteNonQueryAsync();
            await conn.CreateCommand("drop extension if exists hstore cascade").ExecuteNonQueryAsync();
        }

        await using var supplied = NpgsqlDataSource.Create(connectionString);
        await using var store = DocumentStore.For(opts =>
        {
            opts.DatabaseSchemaName = "bug_5898";
            HStore(opts);
            opts.Connection(supplied);
        });

        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var student = new Bug5898Student(Guid.NewGuid());
        await using (var session = store.LightweightSession())
        {
            var e = session.Events.BuildEvent(new Bug5898Enrolled("Alice"));
            e.WithTag(student);
            session.Events.Append(Guid.NewGuid(), e);
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        (await query.Events.QueryByTagsAsync(new EventTagQuery().Or(student))).Count.ShouldBe(1);
    }

    private async Task assertTheStoreRunsOnTheSuppliedDataSource()
    {
        ((PostgresqlDatabase)theStore.Storage.Database).DataSource.ShouldBeSameAs(_supplied);

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var student = new Bug5898Student(Guid.NewGuid());
        await using (var session = theStore.LightweightSession())
        {
            var e = session.Events.BuildEvent(new Bug5898Enrolled("Alice"));
            e.WithTag(student);
            session.Events.Append(Guid.NewGuid(), e);
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession();
        (await query.Events.QueryByTagsAsync(new EventTagQuery().Or(student))).Count.ShouldBe(1);
    }

    public ValueTask InitializeAsync() => default;

    // Marten does not dispose a data source it was handed (#4874), so the test does
    public async ValueTask DisposeAsync()
    {
        if (_store != null) await _store.DisposeAsync();
        await _supplied.DisposeAsync();
    }
}
