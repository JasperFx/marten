using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AspNetCoreWithMarten.Samples.ByStoreOptions
{
    #region sample_AddMartenByStoreOptions
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
            var options = BuildStoreOptions();

            services.AddMarten(options);
        }

        private StoreOptions BuildStoreOptions()
        {
            var connectionString = Configuration.GetConnectionString("postgres");

            // Or lastly, build a StoreOptions object yourself
            var options = new StoreOptions();
            options.Connection(connectionString);

            // Use the more permissive schema auto create behavior
            // while in development
            if (Hosting.IsDevelopment())
            {
                options.AutoCreateSchemaObjects = AutoCreate.All;
            }

            return options;
        }

        // And other methods we don't care about here...
    }
    #endregion sample_AddMartenByStoreOptions


}
