using JasperFx;
using JasperFx.CodeGeneration;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Weasel.Core;
using Weasel.Postgresql;

namespace AspNetCoreWithMarten.Samples.ByNestedClosure;


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
        #region sample_AddMartenByNestedClosure
        var connectionString = Configuration.GetConnectionString("postgres");

        services.AddMarten(opts =>
        {
            opts.Connection(connectionString);
        });

        // In a "Production" environment, we're turning off the
        // automatic database migrations and dynamic code generation
        services.CritterStackDefaults(x =>
        {
            x.Production.GeneratedCodeMode = TypeLoadMode.Static;
            x.Production.ResourceAutoCreate = AutoCreate.None;
        });
        #endregion
    }

    // And other methods we don't care about here...
}
