using System.Data;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Weasel.Postgresql;

namespace AspNetCoreWithMarten.Samples.LightweightSessions;

#region sample_AddMartenWithLightweightSessions

public class Startup
{
    public Startup(IConfiguration configuration, IHostEnvironment hosting)
    {
        Configuration = configuration;
        Hosting = hosting;
    }

    public IConfiguration Configuration { get; }
    public IHostEnvironment Hosting { get; }

    public void ConfigureServices(IServiceCollection services)
    {
        var connectionString = Configuration.GetConnectionString("postgres");

        services.AddMarten(opts =>
            {
                opts.Connection(connectionString);
            })

            // Chained helper to replace the built in
            // session factory behavior
            .UseLightweightSessions();
    }

    // And other methods we don't care about here...
}

#endregion sample_AddMartenWithCustomSessionCreation