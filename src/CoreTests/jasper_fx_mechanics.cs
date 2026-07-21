using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using JasperFx;
using JasperFx.CommandLine.Descriptions;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
        part.SubjectUri.ShouldBe(new Uri("marten://store"));
        part.Title.ShouldBe("Marten");

        var resources = await part.FindResources();
        resources.Single().ShouldBeOfType<DatabaseResource>();

        host.Services.GetServices<IEventStore>().Single().ShouldBeOfType<DocumentStore>();

        var usage = await host.Services.GetRequiredService<IEventStore>().TryCreateUsage(CancellationToken.None);
        usage.SubjectUri.ShouldBe(new Uri("marten://main"));
    }

    [Fact]
    public async Task try_create_usage_populates_projection_error_handling_descriptors()
    {
        // JasperFx/ProductSupport#3 — the projection error policy needs to ride
        // along on EventStoreUsage so monitoring tools can render the right
        // affordance (DLQ button vs "shard halts on error" indicator) without
        // sniffing into Marten internals.
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(m =>
                {
                    m.Connection(ConnectionSource.ConnectionString);
                    m.DatabaseSchemaName = "ps3_error_policy";

                    // Stop-on-error normal-run policy — the reporter's actual
                    // config in PS#3. Without this opt-out, the JasperFx.Events 2.0
                    // default is SkipApplyErrors=true.
                    m.Projections.Errors.SkipApplyErrors = false;
                    m.Projections.Errors.SkipSerializationErrors = false;
                });
            }).StartAsync();

        var usage = await host.Services.GetRequiredService<IEventStore>().TryCreateUsage(CancellationToken.None);

        usage.ProjectionErrors.ShouldNotBeNull();
        usage.ProjectionErrors.SkipApplyErrors.ShouldBeFalse();
        usage.ProjectionErrors.SkipSerializationErrors.ShouldBeFalse();

        // RebuildErrors stays at JasperFx.Events 2.0 defaults (all three false).
        usage.ProjectionRebuildErrors.ShouldNotBeNull();
        usage.ProjectionRebuildErrors.SkipApplyErrors.ShouldBeFalse();
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

    private async Task<IHostBuilder> complexHostBuilder()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        var db1ConnectionString = await CreateDatabaseIfNotExists(conn, "database1");
        var tenant3ConnectionString = await CreateDatabaseIfNotExists(conn, "tenant3");
        var tenant4ConnectionString = await CreateDatabaseIfNotExists(conn, "tenant4");

        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(m =>
                {
                    m.Connection(ConnectionSource.ConnectionString);
                    m.DatabaseSchemaName = "complex_host";

                    m.Schema.For<BasketballTeam>();
                });

                services.AddMartenStore<IFirstStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "first_store_complex";

                    opts.Schema.For<User>();
                });

                services.AddMartenStore<ISecondStore>(services =>
                {
                    var opts = new StoreOptions();
                    opts.DatabaseSchemaName = "second_store_complex";

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

                    opts.Schema.For<Target>();

                    return opts;
                });
            });
    }

    [Fact]
    public async Task use_the_resources_list_command()
    {
        var builder = await complexHostBuilder();
        var result = await builder.RunJasperFxCommands(["resources", "list"]);
        result.ShouldBe(0);
    }

    [Fact]
    public async Task use_the_resources_setup_command()
    {
        var builder = await complexHostBuilder();
        var result = await builder.RunJasperFxCommands(["resources", "setup"]);
        result.ShouldBe(0);
    }

    [Fact]
    public async Task setup_then_clear()
    {
        var result = await (await complexHostBuilder()).RunJasperFxCommands(["resources", "setup"]);
        result.ShouldBe(0);

        var result2 = await (await complexHostBuilder()).RunJasperFxCommands(["resources", "clear"]);
        result2.ShouldBe(0);
    }

    [Fact]
    public async Task setup_then_db_assert()
    {
        var result = await (await complexHostBuilder()).RunJasperFxCommands(["resources", "setup"]);
        result.ShouldBe(0);

        var result2 = await (await complexHostBuilder()).RunJasperFxCommands(["db-assert"]);
        result2.ShouldBe(0);
    }

    [Fact]
    public async Task db_apply_then_db_assert()
    {
        var result = await (await complexHostBuilder()).RunJasperFxCommands(["db-apply"]);
        result.ShouldBe(0);

        var result2 = await (await complexHostBuilder()).RunJasperFxCommands(["db-assert"]);
        result2.ShouldBe(0);
    }

    [Fact]
    public async Task setup_then_teardown()
    {
        var result = await (await complexHostBuilder()).RunJasperFxCommands(["resources", "setup"]);
        result.ShouldBe(0);

        var result2 = await (await complexHostBuilder()).RunJasperFxCommands(["resources", "teardown"]);
        result2.ShouldBe(0);
    }

    [Fact]
    public async Task setup_then_statistics()
    {
        var result = await (await complexHostBuilder()).RunJasperFxCommands(["resources", "setup"]);
        result.ShouldBe(0);

        var result2 = await (await complexHostBuilder()).RunJasperFxCommands(["resources", "statistics"]);
        result2.ShouldBe(0);
    }

    [Fact]
    public async Task setup_then_check()
    {
        var result = await (await complexHostBuilder()).RunJasperFxCommands(["resources", "setup"]);
        result.ShouldBe(0);

        var result2 = await (await complexHostBuilder()).RunJasperFxCommands(["resources", "check"]);
        result2.ShouldBe(0);
    }

    [Fact]
    public async Task enable_advanced_tracking_propagates_to_main_document_store()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddJasperFx(o => o.EnableAdvancedTracking = true);

                services.AddMarten(m =>
                {
                    m.Connection(ConnectionSource.ConnectionString);
                    m.DatabaseSchemaName = "advanced_tracking_main";
                });
            }).StartAsync();

        var store = host.Services.GetRequiredService<IDocumentStore>().As<DocumentStore>();
        store.Options.Events.EnableExtendedProgressionTracking.ShouldBeTrue();
    }

    [Fact]
    public async Task enable_advanced_tracking_propagates_to_ancillary_document_stores()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddJasperFx(o => o.EnableAdvancedTracking = true);

                services.AddMarten(m =>
                {
                    m.Connection(ConnectionSource.ConnectionString);
                    m.DatabaseSchemaName = "advanced_tracking_main";
                });

                services.AddMartenStore<IFirstStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "advanced_tracking_first";
                });

                services.AddMartenStore<ISecondStore>(services =>
                {
                    var opts = new StoreOptions();
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "advanced_tracking_second";
                    return opts;
                });
            }).StartAsync();

        var main = host.Services.GetRequiredService<IDocumentStore>().As<DocumentStore>();
        main.Options.Events.EnableExtendedProgressionTracking.ShouldBeTrue();

        var first = host.Services.GetRequiredService<IFirstStore>().As<DocumentStore>();
        first.Options.Events.EnableExtendedProgressionTracking.ShouldBeTrue();

        var second = host.Services.GetRequiredService<ISecondStore>().As<DocumentStore>();
        second.Options.Events.EnableExtendedProgressionTracking.ShouldBeTrue();
    }

    [Fact]
    public async Task advanced_tracking_off_leaves_extended_progression_tracking_at_default()
    {
        // Sanity: when EnableAdvancedTracking is not set (default false), we do NOT
        // flip EnableExtendedProgressionTracking on. Per-store opt-in is preserved.
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddJasperFx();

                services.AddMarten(m =>
                {
                    m.Connection(ConnectionSource.ConnectionString);
                    m.DatabaseSchemaName = "advanced_tracking_off_main";
                });

                services.AddMartenStore<IFirstStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "advanced_tracking_off_first";
                });
            }).StartAsync();

        var main = host.Services.GetRequiredService<IDocumentStore>().As<DocumentStore>();
        main.Options.Events.EnableExtendedProgressionTracking.ShouldBeFalse();

        var first = host.Services.GetRequiredService<IFirstStore>().As<DocumentStore>();
        first.Options.Events.EnableExtendedProgressionTracking.ShouldBeFalse();
    }

    [Fact]
    public async Task divergent_application_assembly_reuse_warning_is_buffered_and_logged()
    {
        // GH-3521 (jasperfx#543 / marten#4996): a later host that adopts a process-pinned application
        // assembly differing from where it was registered should get a startup warning. Force the
        // divergence by pinning RememberedApplicationAssembly to a DIFFERENT assembly than the one this
        // Marten host is registered from, then assert Marten buffers the warning onto StoreOptions and
        // logs it once from MartenActivator.
        var previous = JasperFxOptions.RememberedApplicationAssembly;
        JasperFxOptions.RememberedApplicationAssembly = typeof(string).Assembly; // System.Private.CoreLib

        var logs = new CapturingLoggerProvider();
        try
        {
            using var host = await Host.CreateDefaultBuilder()
                .ConfigureLogging(logging =>
                {
                    logging.AddProvider(logs);
                    logging.SetMinimumLevel(LogLevel.Trace);
                })
                .ConfigureServices(services =>
                {
                    // ApplyAllDatabaseChangesOnStartup registers MartenActivator as the startup
                    // hosted service — the always-on surface where the warning is logged.
                    services.AddMarten(m =>
                    {
                        m.Connection(ConnectionSource.ConnectionString);
                        m.DatabaseSchemaName = "assembly_reuse_warning";
                    }).ApplyAllDatabaseChangesOnStartup();
                }).StartAsync();

            var store = host.Services.GetRequiredService<IDocumentStore>().As<DocumentStore>();

            // Buffered from JasperFxOptions during ReadJasperFxOptions...
            store.Options.ApplicationAssemblyReuseWarning.ShouldNotBeNull();
            store.Options.ApplicationAssemblyReuseWarning.ShouldContain("System.Private.CoreLib");

            // ...and surfaced once at startup by MartenActivator.
            logs.Messages.ShouldContain(x => x.Contains("System.Private.CoreLib"));
        }
        finally
        {
            JasperFxOptions.RememberedApplicationAssembly = previous;
        }
    }

    [Fact]
    public async Task no_reuse_warning_for_a_normal_single_host()
    {
        // The first (and only) host in the process pins its own assembly and never diverges, so nothing
        // is buffered or logged. Guards against the warning firing spuriously in the common case.
        var previous = JasperFxOptions.RememberedApplicationAssembly;
        JasperFxOptions.RememberedApplicationAssembly = null;

        try
        {
            using var host = await Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddMarten(m =>
                    {
                        m.Connection(ConnectionSource.ConnectionString);
                        m.DatabaseSchemaName = "assembly_reuse_no_warning";
                    });
                }).StartAsync();

            var store = host.Services.GetRequiredService<IDocumentStore>().As<DocumentStore>();
            store.Options.ApplicationAssemblyReuseWarning.ShouldBeNull();
        }
        finally
        {
            JasperFxOptions.RememberedApplicationAssembly = previous;
        }
    }
}

public interface IFirstStore : IDocumentStore{}
public interface ISecondStore : IDocumentStore{}

internal class CapturingLoggerProvider: ILoggerProvider
{
    public ConcurrentQueue<string> Messages { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);

    public void Dispose()
    {
    }

    private class CapturingLogger: ILogger
    {
        private readonly ConcurrentQueue<string> _messages;

        public CapturingLogger(ConcurrentQueue<string> messages)
        {
            _messages = messages;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
            Func<TState, Exception, string> formatter)
        {
            _messages.Enqueue(formatter(state, exception));
        }

        private class NullScope: IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
