using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Lamar;
using LamarCodeGeneration;
using Marten;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Resiliency;
using Marten.Internal;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Weasel.Core;
using Weasel.Core.Migrations;
using Xunit;

namespace CoreTests
{
    public class using_multiple_document_stores_in_same_host : IDisposable
    {
        private readonly Container theContainer;

        // TODO -- need to register additional ICodeFileCollection for the new store
        // TODO -- chained option to add an async daemon for each store
        // TODO -- post-configure options
        // TODO -- LATER, chain IInitialData

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

                services.AddMartenStore<ISecondStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "second_store";
                });
            });
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
            using var session = store.LightweightSession();

            var target = Target.Random();
            session.Store(target);

            await session.SaveChangesAsync();

            using var query = store.QuerySession();
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
                services.AddMarten(ConnectionSource.ConnectionString);

                services.AddMartenStore<IFirstStore>(opts =>
                {
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
        public void using_optimized_mode_in_development()
        {
            using var host = new HostBuilder()
                .ConfigureServices(services =>
                {
                    services.AddMarten(ConnectionSource.ConnectionString).OptimizeArtifactWorkflow();

                    services.AddMartenStore<IFirstStore>(opts =>
                    {
                        opts.Connection(ConnectionSource.ConnectionString);
                        opts.DatabaseSchemaName = "first_store";
                    }).OptimizeArtifactWorkflow();
                })
                .UseEnvironment("Development")
                .UseApplicationProject(GetType().Assembly)
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
                    }).OptimizeArtifactWorkflow();
                })
                .UseEnvironment("Production")
                .UseApplicationProject(GetType().Assembly)
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
                    }).OptimizeArtifactWorkflow(TypeLoadMode.Static);

                    services.SetApplicationProject(typeof(IFirstStore).Assembly);
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
                .OfType<AsyncProjectionHostedService<IFirstStore>>().Single();

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
                        opts.Connection(ConnectionSource.ConnectionString);
                        opts.RegisterDocumentType<User>();
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
    }

    public interface IFirstStore : IDocumentStore{}
    public interface ISecondStore : IDocumentStore{}
}
