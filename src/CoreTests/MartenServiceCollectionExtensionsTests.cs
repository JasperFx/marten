using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Lamar;
using Marten;
using Marten.Internal.Sessions;
using Marten.Services;
using Marten.Sessions;
using Marten.Testing;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Weasel.Core.Migrations;
using Xunit;

namespace CoreTests;

public class MartenServiceCollectionExtensionsTests
{
    // Using Lamar for testing this because of its diagnostics

    [Fact]
    public void add_marten_with_just_connection_string()
    {
        using var container = Container.For(x =>
        {
            x.AddMarten(ConnectionSource.ConnectionString);
        });

        ShouldHaveAllTheExpectedRegistrations(container);
    }

    [Fact]
    public void add_marten_by_store_options()
    {
        using var container = Container.For(x =>
        {
            var options = new StoreOptions();
            options.Connection(ConnectionSource.ConnectionString);
            x.AddMarten(options);
        });

        ShouldHaveAllTheExpectedRegistrations(container);
    }

    [Fact]
    public void add_marten_by_store_options_with_custom_logger()
    {
        using var container = Container.For(x =>
        {
            x.AddMarten(provider =>
            {
                var options = new StoreOptions();
                options.Connection(ConnectionSource.ConnectionString);
                options.Logger(new TestOutputMartenLogger(null));
                return options;
            });
        });

        var store = container.GetRequiredService<IDocumentStore>();
        store.Options.Logger().ShouldBeOfType<TestOutputMartenLogger>();
    }

    [Fact]
    public void add_marten_by_configure_lambda()
    {
        using var container = Container.For(x =>
        {
            x.AddMarten(opts => opts.Connection(ConnectionSource.ConnectionString));
        });

        ShouldHaveAllTheExpectedRegistrations(container);
    }

    [Fact]
    public void picks_up_application_assembly_and_content_directory_from_IHostEnvironment()
    {
        var environment = new MartenHostEnvironment();

        using var host = Host.CreateDefaultBuilder(Array.Empty<string>())
            .ConfigureServices(services =>
            {
                services.AddMarten(ConnectionSource.ConnectionString);

                services.AddSingleton<IHostEnvironment>(environment);
            }).Build();

        var store = host.Services.GetRequiredService<IDocumentStore>().As<DocumentStore>();
        store.Options.ApplicationAssembly.ShouldBe(GetType().Assembly);
        var projectPath = AppContext.BaseDirectory.ParentDirectory().ParentDirectory().ParentDirectory();
        var expectedGeneratedCodeOutputPath = projectPath.ToFullPath().AppendPath("Internal", "Generated");
        store.Options.GeneratedCodeOutputPath.ShouldBe(expectedGeneratedCodeOutputPath);

        var rules = store.Options.CreateGenerationRules();
        rules.ApplicationAssembly.ShouldBe(store.Options.ApplicationAssembly);
        rules.GeneratedCodeOutputPath.ShouldBe(store.Options.GeneratedCodeOutputPath);
    }

    [Fact]
    public void application_assembly_and_content_directory_from_StoreOptions()
    {
        using var host = Host.CreateDefaultBuilder(Array.Empty<string>())
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.SetApplicationProject(GetType().Assembly);
                });
            }).Build();

        var store = host.Services.GetRequiredService<IDocumentStore>().As<DocumentStore>();
        store.Options.ApplicationAssembly.ShouldBe(GetType().Assembly);
        var projectPath = AppContext.BaseDirectory.ParentDirectory().ParentDirectory().ParentDirectory();
        var expectedGeneratedCodeOutputPath = projectPath.ToFullPath().AppendPath("Internal", "Generated");
        store.Options.GeneratedCodeOutputPath.ShouldBe(expectedGeneratedCodeOutputPath);
    }

    [Fact]
    public void no_error_if_IHostEnvironment_does_not_exist()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(ConnectionSource.ConnectionString);
            }).Build();

        var store = host.Services.GetRequiredService<IDocumentStore>().As<DocumentStore>();
        store.Options.ApplicationAssembly.ShouldBe(Assembly.GetEntryAssembly());
        store.Options.GeneratedCodeOutputPath.TrimEnd(Path.DirectorySeparatorChar).ShouldBe(AppContext.BaseDirectory
            .AppendPath("Internal", "Generated").TrimEnd(Path.DirectorySeparatorChar));

        var rules = store.Options.CreateGenerationRules();
        rules.ApplicationAssembly.ShouldBe(store.Options.ApplicationAssembly);
    }

    [Fact]
    public async Task apply_changes_on_startup()
    {
        await using var container = Container.For(services =>
        {
            services.AddLogging();

            #region sample_using_ApplyAllDatabaseChangesOnStartup

            // The normal Marten configuration
            services.AddMarten(opts =>
                {
                    // This helps isolate a test, not something you need to do
                    // in normal usage
                    opts.ApplyChangesLockId += 18;

                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "apply_changes";
                    opts.RegisterDocumentType<User>();
                })

                // Direct the application to apply all outstanding
                // database changes on application startup
                .ApplyAllDatabaseChangesOnStartup();

            #endregion
        });

        var store = container.GetInstance<IDocumentStore>();
        await store.Advanced.Clean.CompletelyRemoveAllAsync();

        var instance = container.Model.For<IHostedService>().Instances.First();
        instance.ImplementationType.ShouldBe(typeof(MartenActivator));
        instance.Lifetime.ShouldBe(ServiceLifetime.Singleton);

        // Just a smoke test here
        await container.GetAllInstances<IHostedService>().First().StartAsync(default);

        await store.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }

    [Fact]
    public async Task assert_configuration_on_startup()
    {
        await using var container = Container.For(services =>
        {
            services.AddLogging();
            services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.RegisterDocumentType<User>();
                    opts.DatabaseSchemaName = "startup";
                })
                .AssertDatabaseMatchesConfigurationOnStartup();
        });

        var store = container.GetInstance<IDocumentStore>();
        await store.Advanced.Clean.CompletelyRemoveAllAsync();

        var instance = container.Model.For<IHostedService>().Instances.First();
        instance.ImplementationType.ShouldBe(typeof(MartenActivator));
        instance.Lifetime.ShouldBe(ServiceLifetime.Singleton);

        await Assert.ThrowsAsync<DatabaseValidationException>(() =>
            container.GetAllInstances<IHostedService>().First().StartAsync(default));
    }

    [Fact]
    public void use_custom_factory_by_type()
    {
        using var container = Container.For(x =>
        {
            x.AddMarten(ConnectionSource.ConnectionString)
                .BuildSessionsWith<SpecialBuilder>();
        });

        ShouldHaveAllTheExpectedRegistrations(container);

        var builder = container.GetInstance<ISessionFactory>()
            .ShouldBeOfType<SpecialBuilder>();

        builder.BuiltQuery.ShouldBeTrue();
        builder.BuiltSession.ShouldBeTrue();
    }

    [Fact]
    public void can_vary_the_scope_of_the_builder()
    {
        using var container = Container.For(x =>
        {
            x.AddMarten(ConnectionSource.ConnectionString)
                .BuildSessionsWith<SpecialBuilder>(ServiceLifetime.Scoped);
        });

        ShouldHaveAllTheExpectedRegistrations(container, ServiceLifetime.Scoped);

        container.Model.For<ISessionFactory>()
            .Default.Lifetime.ShouldBe(ServiceLifetime.Scoped);
    }


    [Fact]
    public void use_lightweight_sessions()
    {
        using var container = Container.For(x =>
        {
            x.AddMarten(ConnectionSource.ConnectionString)
                .UseLightweightSessions();
        });

        ShouldHaveAllTheExpectedRegistrations(container);

        container.Model.For<ISessionFactory>()
            .Default.ImplementationType.ShouldBe(typeof(LightweightSessionFactory));

        container.Model.For<ISessionFactory>()
            .Default.Lifetime.ShouldBe(ServiceLifetime.Singleton);

        using var session = container.GetInstance<IDocumentSession>();
        session.ShouldBeOfType<LightweightSession>();
    }

    [Fact]
    public void apply_configure_marten_options()
    {
        IServiceProvider? provider = null;

        using var container = Container.For(services =>
        {
            services.AddMarten(ConnectionSource.ConnectionString)
                .UseLightweightSessions();

            services.ConfigureMarten(opts => opts.Advanced.HiloSequenceDefaults.MaxLo = 111);

            services.ConfigureMarten((services, opts) =>
            {
                opts.Events.DatabaseSchemaName = "random";
                provider = services;
            });
        });

        var store = container.GetInstance<IDocumentStore>();

        store.Options.Advanced.HiloSequenceDefaults.MaxLo.ShouldBe(111);
        provider.ShouldNotBeNull();
        provider.ShouldBeSameAs(container);

        store.Options.Events.DatabaseSchemaName.ShouldBe("random");
    }

    [Fact]
    public async Task use_npgsql_data_source()
    {
        var services = new ServiceCollection();

        #region sample_using_UseNpgsqlDataSource

        services.AddNpgsqlDataSource(ConnectionSource.ConnectionString);

        services.AddMarten()
            .UseLightweightSessions()
            .UseNpgsqlDataSource();

        #endregion

        var serviceProvider = services.BuildServiceProvider();

        await using var session = serviceProvider.GetService<IDocumentSession>();
        Func<bool> Call(IDocumentSession s) => () => s.Query<Target>().Any();
        Call(session).ShouldNotThrow();
    }

    [Fact]
    public async Task use_npgsql_multi_host_data_source()
    {
        var services = new ServiceCollection();

        #region sample_using_UseNpgsqlDataSourceMultiHost

        services.AddMultiHostNpgsqlDataSource(ConnectionSource.ConnectionString);

        services.AddMarten(x =>
            {
                // Will prefer standby nodes for querying.
                x.Advanced.MultiHostSettings.ReadSessionPreference = TargetSessionAttributes.PreferStandby;
            })
            .UseLightweightSessions()
            .UseNpgsqlDataSource();

        #endregion

        var serviceProvider = services.BuildServiceProvider();

        await using var session = serviceProvider.GetService<IDocumentSession>();
        Func<bool> Call(IDocumentSession s) => () => s.Query<Target>().Any();
        Call(session).ShouldNotThrow();
    }



#if NET8_0
    [Fact]
    public async Task use_npgsql_data_source_with_keyed_registration()
    {
        var services = new ServiceCollection();

        #region sample_using_UseNpgsqlDataSource_keyed

        const string dataSourceKey = "marten_data_source";

        services.AddNpgsqlDataSource(ConnectionSource.ConnectionString, serviceKey: dataSourceKey);

        services.AddMarten()
            .UseLightweightSessions()
            .UseNpgsqlDataSource(dataSourceKey);

        #endregion

        var serviceProvider = services.BuildServiceProvider();

        await using var session = serviceProvider.GetService<IDocumentSession>();
        Func<bool> Call(IDocumentSession s) => () => s.Query<Target>().Any();
        Call(session).ShouldNotThrow();
    }

    [Fact]
    public void use_npgsql_data_source_with_keyed_registration_should_fail_if_key_is_not_passed()
    {
        var services = new ServiceCollection();

        services.AddNpgsqlDataSource(ConnectionSource.ConnectionString, serviceKey: "marten_data_source");

        services.AddMarten()
            .UseLightweightSessions()
            .UseNpgsqlDataSource();

        var serviceProvider = services.BuildServiceProvider();

        Action GetStore(IServiceProvider c) => () =>
        {
            using var store = c.GetService<IDocumentStore>();
        };

        var exc = GetStore(serviceProvider).ShouldThrow<InvalidOperationException>();
        exc.Message.Contains("NpgsqlDataSource").ShouldBeTrue();
    }

    [Fact]
    public void use_npgsql_data_source_with_registration_should_fail_if_key_is_passed()
    {
        var services = new ServiceCollection();

        services.AddNpgsqlDataSource(ConnectionSource.ConnectionString);

        services.AddMarten()
            .UseLightweightSessions()
            .UseNpgsqlDataSource("marten_data_source");

        var serviceProvider = services.BuildServiceProvider();

        Action GetStore(IServiceProvider c) => () =>
        {
            using var store = c.GetService<IDocumentStore>();
        };

        var exc = GetStore(serviceProvider).ShouldThrow<InvalidOperationException>();
        exc.Message.Contains("NpgsqlDataSource").ShouldBeTrue();
    }
#endif

    [Fact]
    public void use_npgsql_data_source_should_fail_if_data_source_is_not_registered()
    {
        var services = new ServiceCollection();

        services.AddMarten()
            .UseLightweightSessions()
            .UseNpgsqlDataSource();

        var serviceProvider = services.BuildServiceProvider();

        Action GetStore(IServiceProvider c) => () =>
        {
            using var store = c.GetService<IDocumentStore>();
        };

        var exc = GetStore(serviceProvider).ShouldThrow<InvalidOperationException>();
        exc.Message.Contains("NpgsqlDataSource").ShouldBeTrue();
    }


    [Fact]
    public void AddMarten_with_no_params_should_fail_if_UseNpgsqlDataSource_was_not_called()
    {
        var services = new ServiceCollection();

        services.AddNpgsqlDataSource(ConnectionSource.ConnectionString);

        services.AddMarten()
            .UseLightweightSessions();

        var serviceProvider = services.BuildServiceProvider();

        Action GetStore(IServiceProvider c) => () =>
        {
            using var store = c.GetService<IDocumentStore>();
        };

        var exc = GetStore(serviceProvider).ShouldThrow<InvalidOperationException>();
        exc.Message.Contains("UseNpgsqlDataSource").ShouldBeTrue();
    }

    public class SpecialBuilder: ISessionFactory
    {
        private readonly IDocumentStore _store;

        public SpecialBuilder(IDocumentStore store)
        {
            _store = store;
        }

        public IQuerySession QuerySession()
        {
            BuiltQuery = true;
            return _store.QuerySession();
        }

        public bool BuiltQuery { get; set; }

        public IDocumentSession OpenSession()
        {
            BuiltSession = true;
            return _store.IdentitySession();
        }

        public bool BuiltSession { get; set; }
    }

    private static void ShouldHaveAllTheExpectedRegistrations(Container container,
        ServiceLifetime factoryLifetime = ServiceLifetime.Singleton)
    {
        container.Model.For<IDocumentStore>().Default.Lifetime.ShouldBe(ServiceLifetime.Singleton);
        container.Model.For<IDocumentSession>().Default.Lifetime.ShouldBe(ServiceLifetime.Scoped);
        container.Model.For<IQuerySession>().Default.Lifetime.ShouldBe(ServiceLifetime.Scoped);

        container.Model.For<ISessionFactory>().Default.Lifetime.ShouldBe(factoryLifetime);

        var store = container.GetInstance<IDocumentStore>();
        store.ShouldNotBeNull();
        container.GetInstance<IDocumentSession>().ShouldNotBeNull();
        container.GetInstance<IQuerySession>().ShouldNotBeNull();

        container.GetInstance<IDatabaseSource>().ShouldBeSameAs(store.As<DocumentStore>().Tenancy);
    }
}
