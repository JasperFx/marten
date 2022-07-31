using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Weasel.Core;
using Weasel.Postgresql;

namespace AspNetCoreWithMarten.Samples.ByNestedClosure
{
    #region sample_AddMartenByNestedClosure
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
            var connectionString = Configuration.GetConnectionString("postgres");

            services.AddMarten(opts =>
            {
                opts.Connection(connectionString);
            })
            // Using the "Optimized artifact workflow" for Marten >= V5
            // sets up your Marten configuration based on your environment
            // See https://martendb.io/configuration/optimized_artifact_workflow.html
            .OptimizeArtifactWorkflow();
        }

        // And other methods we don't care about here...
    }
    #endregion
}
