using JasperFx;
using JasperFx.CodeGeneration;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AspNetCoreWithMarten.Samples.EagerInitialization;


public class Startup
{
    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public IConfiguration Configuration { get; }

    public void ConfigureServices(IServiceCollection services)
    {
        #region sample_AddMartenWithEagerInitialization

        var connectionString = Configuration.GetConnectionString("postgres");

        // By only the connection string
        services.AddMarten(connectionString)
            .InitializeWith();

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

