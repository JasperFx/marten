using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AspNetCoreWithMarten.Samples.EagerInitialization;

#region sample_AddMartenWithEagerInitialization
public class Startup
{
    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public IConfiguration Configuration { get; }

    public void ConfigureServices(IServiceCollection services)
    {

        var connectionString = Configuration.GetConnectionString("postgres");

        // By only the connection string
        services.AddMarten(connectionString)
            // Using the "Optimized artifact workflow" for Marten >= V5
            // sets up your Marten configuration based on your environment
            // See https://martendb.io/configuration/optimized_artifact_workflow.html
            .OptimizeArtifactWorkflow()
            // Spin up the DocumentStore right this second!
            .InitializeWith();
    }

    // And other methods we don't care about here...
}
#endregion
