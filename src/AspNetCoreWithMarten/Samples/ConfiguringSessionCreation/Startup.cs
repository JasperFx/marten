using System.Data;
using JasperFx;
using JasperFx.CodeGeneration;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Weasel.Core;
using Weasel.Postgresql;

namespace AspNetCoreWithMarten.Samples.ConfiguringSessionCreation;

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

#endregion

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
        #region sample_AddMartenWithCustomSessionCreation
        var connectionString = Configuration.GetConnectionString("postgres");

        services.AddMarten(opts =>
            {
                opts.Connection(connectionString);
            })

            // Chained helper to replace the built in
            // session factory behavior
            .BuildSessionsWith<CustomSessionFactory>();

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

