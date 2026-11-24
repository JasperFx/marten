using System;
using System.Threading.Tasks;
using Marten.Events;
using Marten;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Weasel.Postgresql.Connections;
using Xunit;

namespace CoreTests;

/// <summary>
///     #6188: <c>ConfigureNpgsqlDataSourceBuilder()</c> rebuilt the data source factory and threw away one supplied
///     through <c>DataSourceFactory()</c>, so pgvector, PostGIS, NodaTime or the DCB HStore mode configured after it
///     silently dropped the custom factory.
///     <para>
///         #6191: <c>Connection(Func&lt;string, NpgsqlDataSourceBuilder&gt;, NpgsqlDataSource)</c>, which
///         <c>UseNpgsqlDataSource(builderFactory)</c> calls after the <c>AddMarten</c> configuration, replaced the
///         builder factory, losing the builder configuration added before it for every data source Marten builds
///         from it (tenant databases, for example).
///     </para>
/// </summary>
public class data_source_builder_configuration_composes
{
    private static readonly string OtherConnectionString = new NpgsqlConnectionStringBuilder(
        ConnectionSource.ConnectionString) { ApplicationName = "data_source_builder_configuration_composes" }.ConnectionString;

    [Fact]
    public void configuring_the_builder_keeps_a_supplied_data_source_factory()
    {
        var options = new StoreOptions();
        var factory = new RecordingDataSourceFactory();

        options.DataSourceFactory(factory, ConnectionSource.ConnectionString);
        options.ConfigureNpgsqlDataSourceBuilder(_ => { });

        options.NpgsqlDataSourceFactory.ShouldBeSameAs(factory);
    }

    [Fact]
    public void the_hstore_dcb_mode_keeps_a_supplied_data_source_factory()
    {
        var options = new StoreOptions();
        var factory = new RecordingDataSourceFactory();

        options.DataSourceFactory(factory, ConnectionSource.ConnectionString);
        options.Events.DcbStorageMode = DcbStorageMode.HStore;

        options.NpgsqlDataSourceFactory.ShouldBeSameAs(factory);
    }

    [Fact]
    public async Task a_data_source_supplied_after_a_factory_is_still_wrapped_and_configured()
    {
        var options = new StoreOptions();
        options.DataSourceFactory(new RecordingDataSourceFactory());

        await using var supplied = NpgsqlDataSource.Create(ConnectionSource.ConnectionString);
        options.Connection(supplied);

        var applied = 0;
        options.ConfigureNpgsqlDataSourceBuilder(_ => applied++);

        options.NpgsqlDataSourceFactory.Create(supplied.ConnectionString).ShouldBeSameAs(supplied);

        await using var other = options.NpgsqlDataSourceFactory.Create(OtherConnectionString);
        applied.ShouldBe(1);
    }

    [Fact]
    public async Task a_supplied_builder_factory_keeps_builder_configuration_added_before_it()
    {
        var options = new StoreOptions();

        var applied = 0;
        options.ConfigureNpgsqlDataSourceBuilder(_ => applied++);

        var built = 0;
        await using var supplied = NpgsqlDataSource.Create(ConnectionSource.ConnectionString);
        options.Connection(cs =>
        {
            built++;
            return new NpgsqlDataSourceBuilder(cs);
        }, supplied);

        await using var other = options.NpgsqlDataSourceFactory.Create(OtherConnectionString);

        built.ShouldBe(1);
        applied.ShouldBe(1);
    }

    [Fact]
    public async Task a_supplied_builder_factory_keeps_builder_configuration_added_after_it()
    {
        var options = new StoreOptions();

        var built = 0;
        await using var supplied = NpgsqlDataSource.Create(ConnectionSource.ConnectionString);
        options.Connection(cs =>
        {
            built++;
            return new NpgsqlDataSourceBuilder(cs);
        }, supplied);

        var applied = 0;
        options.ConfigureNpgsqlDataSourceBuilder(_ => applied++);

        await using var other = options.NpgsqlDataSourceFactory.Create(OtherConnectionString);

        built.ShouldBe(1);
        applied.ShouldBe(1);
    }

    [Fact]
    public async Task use_npgsql_data_source_with_a_builder_factory_keeps_the_add_marten_builder_configuration()
    {
        var applied = 0;
        var built = 0;

        var services = new ServiceCollection();
        services.AddNpgsqlDataSource(ConnectionSource.ConnectionString);
        services.AddMarten(opts => opts.ConfigureNpgsqlDataSourceBuilder(_ => applied++))
            .UseNpgsqlDataSource(cs =>
            {
                built++;
                return new NpgsqlDataSourceBuilder(cs);
            });

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<StoreOptions>();

        await using var other = options.NpgsqlDataSourceFactory.Create(OtherConnectionString);

        built.ShouldBe(1);
        applied.ShouldBe(1);
    }

    private sealed class RecordingDataSourceFactory: INpgsqlDataSourceFactory
    {
        public NpgsqlDataSource Create(string connectionString) => NpgsqlDataSource.Create(connectionString);
    }
}
