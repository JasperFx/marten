using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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

                // Use the more permissive schema auto create behavior
                // while in development
                if (Hosting.IsDevelopment())
                {
                    opts.AutoCreateSchemaObjects = AutoCreate.All;
                }
            });
        }

        // And other methods we don't care about here...
    }
    #endregion sample_AddMartenByNestedClosure
}
