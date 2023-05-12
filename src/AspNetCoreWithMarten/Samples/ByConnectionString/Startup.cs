using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AspNetCoreWithMarten.Samples.ByConnectionString;

public class Startup
{
    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public IConfiguration Configuration { get; }

    public void ConfigureServices(IServiceCollection services)
    {
        #region sample_AddMartenByConnectionString

        var connectionString = Configuration.GetConnectionString("postgres");


        // By only the connection string
        services.AddMarten(connectionString);

        #endregion
    }

    // And other methods we don't care about here...
}
