using System;
using System.Data;
using Marten;
using Marten.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Weasel.Core;
using Weasel.Postgresql;

namespace AspNetCoreWithMarten.Samples.PerScopeSessionCreation
{
    #region sample_CorrelationIdWithISession
    public interface ISession
    {
        Guid CorrelationId { get; set; }
    }
    #endregion

    #region sample_CorrelatedMartenLogger
    public class CorrelatedMartenLogger: IMartenSessionLogger
    {
        private readonly ILogger<IDocumentSession> _logger;
        private readonly ISession _session;

        public CorrelatedMartenLogger(ILogger<IDocumentSession> logger, ISession session)
        {
            _logger = logger;
            _session = session;
        }

        public void LogSuccess(NpgsqlCommand command)
        {
            // Do some kind of logging using the correlation id of the ISession
        }

        public void LogFailure(NpgsqlCommand command, Exception ex)
        {
            // Do some kind of logging using the correlation id of the ISession
        }

        public void RecordSavedChanges(IDocumentSession session, IChangeSet commit)
        {
            // Do some kind of logging using the correlation id of the ISession
        }

        public void OnBeforeExecute(NpgsqlCommand command)
        {

        }
    }
    #endregion


    #region sample_CustomSessionFactoryByScope
    public class ScopedSessionFactory: ISessionFactory
    {
        private readonly IDocumentStore _store;
        private readonly ILogger<IDocumentSession> _logger;
        private readonly ISession _session;

        // This is important! You will need to use the
        // IDocumentStore to open sessions
        public ScopedSessionFactory(IDocumentStore store, ILogger<IDocumentSession> logger, ISession session)
        {
            _store = store;
            _logger = logger;
            _session = session;
        }

        public IQuerySession QuerySession()
        {
            return _store.QuerySession();
        }

        public IDocumentSession OpenSession()
        {
            var session = _store.LightweightSession();

            // Replace the Marten session logger with our new
            // correlated marten logger
            session.Logger = new CorrelatedMartenLogger(_logger, _session);

            return session;
        }
    }
    #endregion

    #region sample_AddMartenWithCustomSessionCreationByScope
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
                // while in development -- or append .OptimizeArtifactWorkflow()
                // to the .AddMarten() call above and remove this check
                // See https://martendb.io/configuration/optimized_artifact_workflow.html
                if (Hosting.IsDevelopment())
                {
                    opts.AutoCreateSchemaObjects = Weasel.Core.AutoCreate.All;
                }
            })
            // Chained helper to replace the CustomSessionFactory
            .BuildSessionsWith<ScopedSessionFactory>(ServiceLifetime.Scoped);
        }

        // And other methods we don't care about here...
    }
    #endregion
}
