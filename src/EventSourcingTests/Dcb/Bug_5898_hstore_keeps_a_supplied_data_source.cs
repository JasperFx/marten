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
