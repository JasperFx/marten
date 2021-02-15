using System;
using System.Data;
using Marten;
using Marten.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AspNetCoreWithMarten.Samples.ConfiguringSessionCreation
{

    #region sample_CustomSessionFactory
    public class CustomSessionFactory: ISessionFactory
    {
        private readonly IDocumentStore _store;

        // This is important! You will need to use the
        // IDocumentStore to open sessions
        public CustomSessionFactory(IDocumentStore store)
        {
            _store = store;
        }

        public IQuerySession QuerySession()
        {
            return _store.QuerySession();
        }

        public IDocumentSession OpenSession()
        {
            // Opting for the "lightweight" session
            // option with no identity map tracking
            // and choosing to use Serializable transactions
            // just to be different
            return _store.LightweightSession(IsolationLevel.Serializable);
        }
    }
    #endregion sample_CustomSessionFactory

    #region sample_AddMartenWithCustomSessionCreation
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
            })

                // Chained helper to replace the built in
                // session factory behavior
                .BuildSessionsWith<CustomSessionFactory>();
        }

        // And other methods we don't care about here...
    }
    #endregion sample_AddMartenWithCustomSessionCreation
}
