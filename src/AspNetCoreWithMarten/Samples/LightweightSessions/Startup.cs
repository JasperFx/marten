using Marten;

namespace AspNetCoreWithMarten.Samples.LightweightSessions;

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
        #region sample_AddMartenWithLightweightSessions
        var connectionString = Configuration.GetConnectionString("postgres");

        services.AddMarten(opts =>
            {
                opts.Connection(connectionString);
            })

            // Chained helper to replace the built in
            // session factory behavior
            .UseLightweightSessions();

        #endregion
    }

    // And other methods we don't care about here...
}

