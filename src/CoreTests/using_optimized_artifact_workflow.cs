using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using Lamar;
using JasperFx.CodeGeneration;
using JasperFx.Core.Reflection;
using Marten;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace CoreTests;

public class using_optimized_artifact_workflow
{
    [Fact]
    public void all_the_defaults()
    {
        using var container = Container.For(services =>
        {
            services.AddMarten(ConnectionSource.ConnectionString);
        });


        var store = container.GetInstance<IDocumentStore>().As<DocumentStore>();

        var rules = store.Options.CreateGenerationRules();

        store.Options.AutoCreateSchemaObjects.ShouldBe(AutoCreate.CreateOrUpdate);
        store.Options.GeneratedCodeMode.ShouldBe(TypeLoadMode.Dynamic);

        rules.GeneratedNamespace.ShouldBe("Marten.Generated");
        rules.SourceCodeWritingEnabled.ShouldBeTrue();

    }

    public static async Task bootstrapping_example()
    {
        #region sample_using_optimized_artifact_workflow

        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten("connection string");

                // In a "Production" environment, we're turning off the
                // automatic database migrations and dynamic code generation
                services.CritterStackDefaults(x =>
                {
                    x.Production.GeneratedCodeMode = TypeLoadMode.Static;
                    x.Production.ResourceAutoCreate = AutoCreate.None;
                });
            }).StartAsync();

        #endregion
    }

    public static async Task bootstrapping_example_with_static()
    {
        #region sample_using_optimized_artifact_workflow_static

        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten("connection string");

                // In a "Production" environment, we're turning off the
                // automatic database migrations and dynamic code generation
                services.CritterStackDefaults(x =>
                {
                    x.Production.GeneratedCodeMode = TypeLoadMode.Static;
                    x.Production.ResourceAutoCreate = AutoCreate.None;
                });
            }).StartAsync();
        #endregion
    }

    [Fact]
    public void using_optimized_mode_in_development()
    {
        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(ConnectionSource.ConnectionString);

                // In a "Production" environment, we're turning off the
                // automatic database migrations and dynamic code generation
                services.CritterStackDefaults(x =>
                {
                    x.Production.GeneratedCodeMode = TypeLoadMode.Static;
                    x.Production.ResourceAutoCreate = AutoCreate.None;

                    x.Development.GeneratedCodeMode = TypeLoadMode.Auto;
                    x.Development.ResourceAutoCreate = AutoCreate.CreateOrUpdate;
                });
            })
            .UseEnvironment("Development")
            .Start();


        var store = host.Services.GetRequiredService<IDocumentStore>().As<DocumentStore>();

        var rules = store.Options.CreateGenerationRules();

        store.Options.AutoCreateSchemaObjects.ShouldBe(AutoCreate.CreateOrUpdate);
        store.Options.GeneratedCodeMode.ShouldBe(TypeLoadMode.Auto);

        rules.GeneratedNamespace.ShouldBe("Marten.Generated");
        rules.SourceCodeWritingEnabled.ShouldBeTrue();
    }

    [Fact]
    public void using_optimized_mode_in_production()
    {
        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(ConnectionSource.ConnectionString);

                // In a "Production" environment, we're turning off the
                // automatic database migrations and dynamic code generation
                services.CritterStackDefaults(x =>
                {
                    x.Production.GeneratedCodeMode = TypeLoadMode.Auto;
                    x.Production.ResourceAutoCreate = AutoCreate.None;
                    x.Production.SourceCodeWritingEnabled = false;
                });
            })
            .UseEnvironment("Production")
            .Start();


        var store = host.Services.GetRequiredService<IDocumentStore>().As<DocumentStore>();

        var rules = store.Options.CreateGenerationRules();

        store.Options.AutoCreateSchemaObjects.ShouldBe(AutoCreate.None);
        store.Options.GeneratedCodeMode.ShouldBe(TypeLoadMode.Auto);

        rules.GeneratedNamespace.ShouldBe("Marten.Generated");
        rules.SourceCodeWritingEnabled.ShouldBeFalse();
    }

    public static void default_setup()
    {
        #region sample_simplest_possible_setup

        var store = DocumentStore.For("connection string");

        #endregion
    }

    [Fact]
    public void using_optimized_mode_in_production_override_type_load_mode()
    {
        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(ConnectionSource.ConnectionString);

                // In a "Production" environment, we're turning off the
                // automatic database migrations and dynamic code generation
                services.CritterStackDefaults(x =>
                {
                    x.Production.GeneratedCodeMode = TypeLoadMode.Static;
                    x.Production.ResourceAutoCreate = AutoCreate.None;
                    x.Production.SourceCodeWritingEnabled = false;
                });
            })
            .UseEnvironment("Production")
            .Start();


        var store = host.Services.GetRequiredService<IDocumentStore>().As<DocumentStore>();

        var rules = store.Options.CreateGenerationRules();

        store.Options.AutoCreateSchemaObjects.ShouldBe(AutoCreate.None);
        store.Options.GeneratedCodeMode.ShouldBe(TypeLoadMode.Static);

        rules.GeneratedNamespace.ShouldBe("Marten.Generated");
        rules.SourceCodeWritingEnabled.ShouldBeFalse();
    }

    [Fact]
    public void using_optimized_mode_in_production_override_environment_name()
    {
        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(ConnectionSource.ConnectionString);

                // In a "Production" environment, we're turning off the
                // automatic database migrations and dynamic code generation
                services.CritterStackDefaults(x =>
                {
                    x.Production.GeneratedCodeMode = TypeLoadMode.Static;
                    x.Production.ResourceAutoCreate = AutoCreate.CreateOrUpdate;

                    x.Development.GeneratedCodeMode = TypeLoadMode.Auto;

                    x.DevelopmentEnvironmentName = "Local";
                });
            })
            .UseEnvironment("Local")
            .Start();

        var store = host.Services.GetRequiredService<IDocumentStore>().As<DocumentStore>();

        var rules = store.Options.CreateGenerationRules();

        store.Options.AutoCreateSchemaObjects.ShouldBe(AutoCreate.CreateOrUpdate);
        store.Options.GeneratedCodeMode.ShouldBe(TypeLoadMode.Auto);

        rules.GeneratedNamespace.ShouldBe("Marten.Generated");
        rules.SourceCodeWritingEnabled.ShouldBeTrue();
    }
}
