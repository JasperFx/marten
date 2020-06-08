using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AspNetCoreWithMarten.Samples.ByConnectionString
{
    // SAMPLE: AddMartenByConnectionString
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
            services.AddMarten(connectionString);
        }

        // And other methods we don't care about here...
    }
    // ENDSAMPLE
}
