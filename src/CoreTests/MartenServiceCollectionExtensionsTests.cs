using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Lamar;
using Marten;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
            x.AddMarten(provider => {
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
        store.Options.GeneratedCodeOutputPath.TrimEnd(Path.DirectorySeparatorChar).ShouldBe(AppContext.BaseDirectory.AppendPath("Internal", "Generated").TrimEnd(Path.DirectorySeparatorChar));

        var rules = store.Options.CreateGenerationRules();
        rules.ApplicationAssembly.ShouldBe(store.Options.ApplicationAssembly);
    }

    [Fact]
    public void eager_initialization_of_the_store()
    {
        IDocumentStore store = null;

        using var container = Container.For(x =>
        {
            store = x.AddMarten(ConnectionSource.ConnectionString)
                .InitializeStore();
        });

        ShouldHaveAllTheExpectedRegistrations(container);

        container.GetInstance<IDocumentStore>().ShouldBeSameAs(store);
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
                    opts.Connection(ConnectionSource.ConnectionString);
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
                })
                .AssertDatabaseMatchesConfigurationOnStartup();
        });

        var store = container.GetInstance<IDocumentStore>();
        await store.Advanced.Clean.CompletelyRemoveAllAsync();

        var instance = container.Model.For<IHostedService>().Instances.First();
        instance.ImplementationType.ShouldBe(typeof(MartenActivator));
        instance.Lifetime.ShouldBe(ServiceLifetime.Singleton);

        await Assert.ThrowsAsync<DatabaseValidationException>(() => container.GetAllInstances<IHostedService>().First().StartAsync(default));
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
            return _store.OpenSession();
        }

        public bool BuiltSession { get; set; }
    }

    private static void ShouldHaveAllTheExpectedRegistrations(Container container, ServiceLifetime factoryLifetime = ServiceLifetime.Singleton)
    {
        container.Model.For<IDocumentStore>().Default.Lifetime.ShouldBe(ServiceLifetime.Singleton);
        container.Model.For<IDocumentSession>().Default.Lifetime.ShouldBe(ServiceLifetime.Scoped);
        container.Model.For<IQuerySession>().Default.Lifetime.ShouldBe(ServiceLifetime.Scoped);

        container.Model.For<ISessionFactory>().Default.Lifetime.ShouldBe(factoryLifetime);

        var store = container.GetInstance<IDocumentStore>();
        store.ShouldNotBeNull();
        container.GetInstance<IDocumentSession>().ShouldNotBeNull();
        container.GetInstance<IQuerySession>().ShouldNotBeNull();

        container.GetInstance<IDatabaseSource>().ShouldBeTheSameAs(store.As<DocumentStore>().Tenancy);
    }


}
