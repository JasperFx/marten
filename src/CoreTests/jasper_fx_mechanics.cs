using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.CommandLine.Descriptions;
using JasperFx.Events;
using Marten;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Weasel.Core.CommandLine;
using Weasel.Postgresql;
using Weasel.Postgresql.Migrations;
using Xunit;

namespace CoreTests;

public class jasper_fx_mechanics
{
    private async Task<string> CreateDatabaseIfNotExists(NpgsqlConnection conn, string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString);

        var exists = await conn.DatabaseExists(databaseName);
        if (!exists)
        {
            await new DatabaseSpecification().BuildDatabase(conn, databaseName);
        }

        builder.Database = databaseName;

        return builder.ConnectionString;
    }

    [Fact]
    public async Task build_system_part_for_single_document_store_and_single_tenancy()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(m =>
                {
                    m.Connection(ConnectionSource.ConnectionString);
                    m.DatabaseSchemaName = "system_part";
                });
            }).StartAsync();

        var part = host.Services
            .GetServices<ISystemPart>()
            .OfType<MartenSystemPart>()
            .SingleOrDefault();

        part.ShouldNotBeNull();
        part.SubjectUri.ShouldBe(new Uri("marten://documentstore"));
        part.Title.ShouldBe("Marten");

        var resources = await part.FindResources();
        resources.Single().ShouldBeOfType<DatabaseResource>();

        host.Services.GetServices<IEventStore>().Single().ShouldBeOfType<DocumentStore>();

        var usage = await host.Services.GetRequiredService<IEventStore>().TryCreateUsage(CancellationToken.None);
        usage.SubjectUri.ShouldBe(new Uri("marten://main"));
    }

    [Fact]
    public async Task run_describe_with_single_document_store_and_single_tenancy()
    {
        var result = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(m =>
                {
                    m.Connection(ConnectionSource.ConnectionString);
                    m.DatabaseSchemaName = "system_part";
                });
            }).RunJasperFxCommands(["describe"]);

        result.ShouldBe(0);
    }

    [Fact]
    public async Task build_system_parts_for_ancillary_document_stores_and_single_tenancy()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMartenStore<IFirstStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "first_store";
                });

                services.AddMartenStore<ISecondStore>(services =>
                {
                    var opts = new StoreOptions();
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "second_store";

                    return opts;
                });
            }).StartAsync();

        var part1 = host.Services
            .GetServices<ISystemPart>()
            .OfType<MartenSystemPart<IFirstStore>>()
            .SingleOrDefault();

        part1.ShouldNotBeNull();
        part1.SubjectUri.ShouldBe(new Uri("marten://ifirststore"));
        part1.Title.ShouldBe("Marten IFirstStore");

        var part2 = host.Services
            .GetServices<ISystemPart>()
            .OfType<MartenSystemPart<ISecondStore>>()
            .SingleOrDefault();

        part2.ShouldNotBeNull();
        part2.SubjectUri.ShouldBe(new Uri("marten://isecondstore"));
        part2.Title.ShouldBe("Marten ISecondStore");

        host.Services.GetServices<IEventStore>().OfType<IFirstStore>().Any().ShouldBeTrue();
        host.Services.GetServices<IEventStore>().OfType<ISecondStore>().Any().ShouldBeTrue();

        var usage1 = await host.Services.GetServices<IEventStore>().Single(x => x is IFirstStore)
            .TryCreateUsage(CancellationToken.None);

        usage1.SubjectUri.ShouldBe(new Uri("marten://ifirststore"));

        var usage2 = await host.Services.GetServices<IEventStore>().Single(x => x is ISecondStore)
            .TryCreateUsage(CancellationToken.None);

        usage2.SubjectUri.ShouldBe(new Uri("marten://isecondstore"));
    }

    [Fact]
    public async Task integration_with_describe_command_line()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        var db1ConnectionString = await CreateDatabaseIfNotExists(conn, "database1");
        var tenant3ConnectionString = await CreateDatabaseIfNotExists(conn, "tenant3");
        var tenant4ConnectionString = await CreateDatabaseIfNotExists(conn, "tenant4");


        var result = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMartenStore<IFirstStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "first_store";
                });

                services.AddMartenStore<ISecondStore>(services =>
                {
                    var opts = new StoreOptions();
                    opts.DatabaseSchemaName = "second_store";

                    // Explicitly map tenant ids to database connection strings
                    opts.MultiTenantedDatabases(x =>
                    {
                        // Map multiple tenant ids to a single named database
                        x.AddMultipleTenantDatabase(db1ConnectionString, "database1")
                            .ForTenants("tenant1", "tenant2");

                        // Map a single tenant id to a database, which uses the tenant id as well for the database identifier
                        x.AddSingleTenantDatabase(tenant3ConnectionString, "tenant3");
                        x.AddSingleTenantDatabase(tenant4ConnectionString, "tenant4");
                    });

                    return opts;
                });
            }).RunJasperFxCommands(["describe"]);

        result.ShouldBe(0);
    }
}

public interface IFirstStore : IDocumentStore{}
public interface ISecondStore : IDocumentStore{}
