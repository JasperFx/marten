using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.CodeGeneration;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Events.Daemon;
using Lamar;
using Marten;
using Marten.Events.Daemon.Coordination;
using Marten.Internal;
using Marten.Services;
using Marten.Testing;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Weasel.Core;
using Weasel.Core.Migrations;
using Xunit;

namespace StressTests;

public class using_multiple_document_stores_in_same_host : IDisposable
{
    private readonly Container theContainer;

    public using_multiple_document_stores_in_same_host()
    {
        theContainer = Container.For(services =>
        {
            // Mostly just to prove we can mix and match
            services.AddMarten(ConnectionSource.ConnectionString);

            services.AddMartenStore<IFirstStore>(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
                opts.DatabaseSchemaName = "first_store";
                opts.GeneratedCodeMode = TypeLoadMode.Auto;
            });

            // Just to prove that this doesn't blow up, see GH-2892
            services.AddKeyedSingleton<IMartenSessionLogger>("blue", new RecordingLogger());

            services.AddMartenStore<ISecondStore>(services =>
            {
                var opts = new StoreOptions();
                opts.Connection(ConnectionSource.ConnectionString);
                opts.DatabaseSchemaName = "second_store";

                return opts;
            });
        });
    }

    [Fact]
    public void get_all_document_stores()
    {
        var allDocumentStores = theContainer.AllDocumentStores();
        allDocumentStores.Count.ShouldBe(3);
    }

    [Fact]
    public void has_a_registration_for_IDatabaseSource_for_tenancy()
    {
        var store1 = theContainer.GetInstance<IFirstStore>().As<DocumentStore>();
        var store2 = theContainer.GetInstance<ISecondStore>().As<DocumentStore>();
        var sources = theContainer.GetAllInstances<IDatabaseSource>();

        sources.ShouldContain(store1.Tenancy);
        sources.ShouldContain(store2.Tenancy);
    }

    [Fact]
    public void should_have_a_single_registration_for_each_secondary_stores()
    {
        theContainer.Model.HasRegistrationFor<IFirstStore>().ShouldBeTrue();
        theContainer.Model.HasRegistrationFor<ISecondStore>().ShouldBeTrue();
    }

    [Fact]
    public void should_have_a_single_ICodeFileCollection_registration_for_secondary_stores()
    {
        theContainer.GetAllInstances<ICodeFileCollection>().OfType<SecondaryDocumentStores>()
            .Count().ShouldBe(1);
    }

    [Fact]
    public void can_build_both_stores()
    {
        theContainer.GetInstance<IFirstStore>().ShouldNotBeNull();
        theContainer.GetInstance<ISecondStore>().ShouldNotBeNull();
    }

    [Fact]
    public async Task use_the_generated_store()
    {
        var store = theContainer.GetInstance<IFirstStore>();
        await using var session = store.LightweightSession();

        var target = Target.Random();
        session.Store(target);

        await session.SaveChangesAsync();

        await using var query = store.QuerySession();
        var target2 = await query.LoadAsync<Target>(target.Id);
        target2.ShouldNotBeNull();
    }

    public void Dispose()
    {
        theContainer?.Dispose();
    }
}

public class additional_document_store_registration_and_optimized_artifact_workflow
{
    [Fact]
    public void all_the_defaults()
    {
        using var container = Container.For(services =>
        {
            services.AddMarten(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
            });

            services.AddMartenStore<IFirstStore>(opts =>
            {
                opts.ApplyChangesLockId += 17;
                opts.Connection(ConnectionSource.ConnectionString);
                opts.DatabaseSchemaName = "first_store";
            });
        });


        var store = container.GetInstance<IFirstStore>().As<DocumentStore>();

        var rules = store.Options.CreateGenerationRules();

        store.Options.AutoCreateSchemaObjects.ShouldBe(AutoCreate.CreateOrUpdate);
        store.Options.GeneratedCodeMode.ShouldBe(TypeLoadMode.Dynamic);

        rules.GeneratedNamespace.ShouldBe("Marten.Generated.IFirstStore");
        rules.GeneratedCodeOutputPath.ShouldEndWith(Path.Combine("Internal", "Generated", "IFirstStore"));
        rules.SourceCodeWritingEnabled.ShouldBeTrue();

    }

    [Fact]
    public async Task can_resolve_all_stores()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(ConnectionSource.ConnectionString);

                services.AddMartenStore<IFirstStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "first_store";
                });
            }).StartAsync();

        var stores = host.AllDocumentStores();
        stores.Count.ShouldBe(2);
        stores.OfType<IFirstStore>().Count().ShouldBe(1);
    }

    [Fact]
    public void using_optimized_mode_in_development()
    {
        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.SetApplicationProject(GetType().Assembly);
                }).OptimizeArtifactWorkflow();

                services.AddMartenStore<IFirstStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "first_store";
                    opts.SetApplicationProject(GetType().Assembly);
                }).OptimizeArtifactWorkflow();


            })
            .UseEnvironment("Development")
            .Start();


        var store = host.Services.GetRequiredService<IFirstStore>().As<DocumentStore>();

        var rules = store.Options.CreateGenerationRules();

        store.Options.AutoCreateSchemaObjects.ShouldBe(AutoCreate.CreateOrUpdate);
        store.Options.GeneratedCodeMode.ShouldBe(TypeLoadMode.Auto);

        rules.GeneratedNamespace.ShouldBe("Marten.Generated.IFirstStore");
        rules.GeneratedCodeOutputPath.ShouldEndWith(Path.Combine("Internal", "Generated", "IFirstStore"));
        rules.SourceCodeWritingEnabled.ShouldBeTrue();
    }

    [Fact]
    public void using_optimized_mode_in_production()
    {
        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(ConnectionSource.ConnectionString).OptimizeArtifactWorkflow();

                services.AddMartenStore<IFirstStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "first_store";
                    opts.SetApplicationProject(GetType().Assembly);
                }).OptimizeArtifactWorkflow();
            })
            .UseEnvironment("Production")
            .Start();


        var store = host.Services.GetRequiredService<IFirstStore>().As<DocumentStore>();

        var rules = store.Options.CreateGenerationRules();

        store.Options.AutoCreateSchemaObjects.ShouldBe(AutoCreate.None);
        store.Options.GeneratedCodeMode.ShouldBe(TypeLoadMode.Auto);

        rules.GeneratedNamespace.ShouldBe("Marten.Generated.IFirstStore");
        rules.GeneratedCodeOutputPath.ShouldEndWith(Path.Combine("Internal", "Generated", "IFirstStore"));
        rules.SourceCodeWritingEnabled.ShouldBeFalse();
    }

    [Fact]
    public void picks_up_application_assembly_and_content_directory_from_IHostEnvironment()
    {
        var environment = new MartenHostEnvironment();

        using var host = Host.CreateDefaultBuilder(Array.Empty<string>())
            .ConfigureServices(services =>
            {
                services.AddMarten(ConnectionSource.ConnectionString);
                services.AddMartenStore<IFirstStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                });

                services.AddSingleton<IHostEnvironment>(environment);
            })
            .Build();

        var store = host.Services.GetRequiredService<IFirstStore>().As<DocumentStore>();
        store.Options.ApplicationAssembly.ShouldBe(GetType().Assembly);
        store.Options.GeneratedCodeOutputPath.ShouldBe(environment.ContentRootPath.ToFullPath().AppendPath("Internal", "Generated"));

        var rules = store.Options.CreateGenerationRules();
        rules.ApplicationAssembly.ShouldBe(store.Options.ApplicationAssembly);
        rules.GeneratedCodeOutputPath.ShouldBe(store.Options.GeneratedCodeOutputPath.AppendPath("IFirstStore"));
    }

    [Fact]
    public void using_optimized_mode_in_production_override_type_load_mode()
    {
        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                // Have to help .net here understand what the environment *should* be
                services.AddSingleton<IHostEnvironment>(new MartenHostEnvironment
                {
                    EnvironmentName = "Production"
                });


                services.AddMarten(ConnectionSource.ConnectionString).OptimizeArtifactWorkflow(TypeLoadMode.Static);

                services.AddMartenStore<IFirstStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "first_store";
                    opts.SetApplicationProject(typeof(IFirstStore).Assembly);
                }).OptimizeArtifactWorkflow(TypeLoadMode.Static);

            })
            .Start();


        var store = host.Services.GetRequiredService<IFirstStore>().As<DocumentStore>();

        var rules = store.Options.CreateGenerationRules();

        store.Options.AutoCreateSchemaObjects.ShouldBe(AutoCreate.None);
        store.Options.GeneratedCodeMode.ShouldBe(TypeLoadMode.Static);

        rules.GeneratedNamespace.ShouldBe("Marten.Generated.IFirstStore");
        rules.GeneratedCodeOutputPath.ShouldEndWith(Path.Combine("Internal", "Generated", "IFirstStore"));
        rules.SourceCodeWritingEnabled.ShouldBeFalse();
    }

    [Fact]
    public void use_secondary_options_configure_against_additional_store()
    {
        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(ConnectionSource.ConnectionString).OptimizeArtifactWorkflow(TypeLoadMode.Static);

                services.AddMartenStore<IFirstStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "first_store";
                });

                // Override configuration of IFirstStore
                services.ConfigureMarten<IFirstStore>(opts =>
                {
                    opts.AutoCreateSchemaObjects = AutoCreate.None;
                });
            })
            .Start();


        var store = host.Services.GetRequiredService<IFirstStore>().As<DocumentStore>();
        store.Options.AutoCreateSchemaObjects.ShouldBe(AutoCreate.None);

    }

    [Fact]
    public void bootstrap_async_daemon_for_secondary_store()
    {
        using var host = new HostBuilder()

            .ConfigureServices(services =>
            {
                services.AddMarten(ConnectionSource.ConnectionString).OptimizeArtifactWorkflow(TypeLoadMode.Static);

                services.AddMartenStore<IFirstStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "first_store";
                }).AddAsyncDaemon(DaemonMode.HotCold);


            })
            .Start();

        var store = host.Services.GetRequiredService<IFirstStore>().As<DocumentStore>();
        store.Options.Projections.AsyncMode.ShouldBe(DaemonMode.HotCold);

        var hostedService = host.Services.GetServices<IHostedService>()
            .OfType<ProjectionCoordinator<IFirstStore>>().Single();

        (hostedService.Store is IFirstStore).ShouldBeTrue();


    }

    [Fact]
    public async Task apply_changes_on_startup()
    {
        await using var container = Container.For(x =>
        {
            x.AddLogging();
            x.AddMartenStore<IFirstStore>(opts =>
                {
                    opts.ApplyChangesLockId += 19;
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.RegisterDocumentType<User>();
                    opts.DatabaseSchemaName = "first_store";
                })
                .ApplyAllDatabaseChangesOnStartup();
        });

        var store = container.GetInstance<IFirstStore>();
        await store.Advanced.Clean.CompletelyRemoveAllAsync();

        var instance = container.Model.For<IHostedService>().Instances.First();
        instance.ImplementationType.ShouldBe(typeof(MartenActivator<IFirstStore>));
        instance.Lifetime.ShouldBe(ServiceLifetime.Singleton);

        // Just a smoke test here
        await container.GetAllInstances<IHostedService>().First().StartAsync(default);

        await store.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }

    [Fact]
    public async Task jasperfx_options_usage_with_ancillary_stores()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(ConnectionSource.ConnectionString);

                services.AddMartenStore<IFirstStore>(opts =>
                    {
                        opts.ApplyChangesLockId += 19;
                        opts.Connection(ConnectionSource.ConnectionString);
                        opts.RegisterDocumentType<User>();
                        opts.DatabaseSchemaName = "first_store";
                    })
                    .ApplyAllDatabaseChangesOnStartup();

                services.AddJasperFx(opts =>
                {
                    opts.Development.AutoCreate = AutoCreate.None;
                    opts.GeneratedCodeOutputPath = "/";
                    opts.Development.GeneratedCodeMode = TypeLoadMode.Auto;

                    // Default is true
                    opts.Development.SourceCodeWritingEnabled = false;
                });
            })
            .UseEnvironment("Development").StartAsync();

        var store = (DocumentStore)host.DocumentStore<IFirstStore>();
        store.Options.AutoCreateSchemaObjects.ShouldBe(AutoCreate.None);
        store.Options.SourceCodeWritingEnabled.ShouldBeFalse();
        store.Options.GeneratedCodeMode.ShouldBe(TypeLoadMode.Auto);
    }
}

public interface IFirstStore : IDocumentStore{}
public interface ISecondStore : IDocumentStore{}
