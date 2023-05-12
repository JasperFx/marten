using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Weasel.Core;
using Weasel.Postgresql;

namespace AspNetCoreWithMarten.Samples.ByStoreOptions;

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
        #region sample_AddMartenByStoreOptions
        var connectionString = Configuration.GetConnectionString("postgres");

        // Build a StoreOptions object yourself
        var options = new StoreOptions();
        options.Connection(connectionString);


        services.AddMarten(options)
            // Using the "Optimized artifact workflow" for Marten >= V5
            // sets up your Marten configuration based on your environment
            // See https://martendb.io/configuration/optimized_artifact_workflow.html
            .OptimizeArtifactWorkflow();

        #endregion
    }

    // And other methods we don't care about here...
}
