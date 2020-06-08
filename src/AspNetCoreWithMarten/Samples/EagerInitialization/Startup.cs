using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AspNetCoreWithMarten.Samples.EagerInitialization
{
    // SAMPLE: AddMartenWithEagerInitialization
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

                // Spin up the DocumentStore right this second!
                .InitializeStore();
        }

        // And other methods we don't care about here...
    }
    // ENDSAMPLE
}
