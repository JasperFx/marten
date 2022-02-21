using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Lamar;
using LamarCodeGeneration;
using Marten;
using Marten.Internal;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Weasel.Core;
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
        public void should_have_a_single_registration_for_each_secondary_stores()
        {
            theContainer.Model.HasRegistrationFor<IFirstStore>().ShouldBeTrue();
            theContainer.Model.HasRegistrationFor<ISecondStore>().ShouldBeTrue();
        }

        [Fact]
        public void should_have_a_single_ICodeFileCollection_registration_for_secondary_stores()
        {
            theContainer.Model.InstancesOf<ICodeFileCollection>()
                .Count(x => x.ImplementationType == typeof(SecondaryDocumentStores)).ShouldBe(1);
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
            var environment = new TestHostEnvironment();

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
            store.Options.GeneratedCodeOutputPath.ShouldBe(environment.ContentRootPath.ToFullPath());

            var rules = store.Options.CreateGenerationRules();
            rules.ApplicationAssembly.ShouldBe(store.Options.ApplicationAssembly);
            rules.GeneratedCodeOutputPath.ShouldBe(store.Options.GeneratedCodeOutputPath.AppendPath("Internal", "Generated", "IFirstStore"));
        }

        [Fact]
        public void using_optimized_mode_in_production_override_type_load_mode()
        {
            using var host = new HostBuilder()
                .ConfigureServices(services =>
                {
                    // Have to help .net here understand what the environment *should* be
                    services.AddSingleton<IHostEnvironment>(new TestHostEnvironment
                    {
                        EnvironmentName = "Production"
                    });


                    services.AddMarten(ConnectionSource.ConnectionString).OptimizeArtifactWorkflow(TypeLoadMode.Static);

                    services.AddMartenStore<IFirstStore>(opts =>
                    {
                        opts.Connection(ConnectionSource.ConnectionString);
                        opts.DatabaseSchemaName = "first_store";
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
    }

    public interface IFirstStore : IDocumentStore{}
    public interface ISecondStore : IDocumentStore{}
}
