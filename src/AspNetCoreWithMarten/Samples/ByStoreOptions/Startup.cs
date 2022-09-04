using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Weasel.Core;
using Weasel.Postgresql;

namespace AspNetCoreWithMarten.Samples.ByStoreOptions;

#region sample_AddMartenByStoreOptions
public class Startup
{
    public IConfiguration Configuration { get; }
    public IHostEnvironment Hosting { get; }

    public Startup(IConfiguration configuration, IHostEnvironment hosting)
    {
        Configuration = configuration;
        Hosting = hosting;
    }

    public void ConfigureServices(IServiceCollection services)
    {
        var options = BuildStoreOptions();

        services.AddMarten(options)
            // Using the "Optimized artifact workflow" for Marten >= V5
            // sets up your Marten configuration based on your environment
            // See https://martendb.io/configuration/optimized_artifact_workflow.html
            .OptimizeArtifactWorkflow();
    }

    private StoreOptions BuildStoreOptions()
    {
        var connectionString = Configuration.GetConnectionString("postgres");

        // Or lastly, build a StoreOptions object yourself
        var options = new StoreOptions();
        options.Connection(connectionString);
        return options;
    }

    // And other methods we don't care about here...
}
#endregion