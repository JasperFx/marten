using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Marten.Internal.CodeGeneration;
using Marten.Sessions;
using Npgsql;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace Marten.Testing.Harness
{
    /// <summary>
    /// Use this if the tests in a fixture are going to use
    /// all custom StoreOptions configuration
    /// </summary>
    [Collection("OneOffs")]
    public abstract class OneOffConfigurationsContext: IDisposable
    {
        protected string _schemaName;
        private DocumentStore _store;
        private IDocumentSession _session;
        protected readonly IList<IDisposable> _disposables = new List<IDisposable>();

        public string SchemaName => _schemaName;

        protected OneOffConfigurationsContext()
        {
            _schemaName = GetType().Name.ToLower().Sanitize();
        }

        public IList<IDisposable> Disposables => _disposables;

        /// <summary>
        /// This will create an additional DocumentStore with the same schema name
        /// The base context will track it and dispose it later
        ///
        /// This is meant for tests on schema detection and migrations
        /// </summary>
        /// <param name="configure"></param>
        /// <returns></returns>
        protected DocumentStore SeparateStore(Action<StoreOptions> configure = null)
        {
            var options = new StoreOptions { DatabaseSchemaName = SchemaName };

            options.Connection(ConnectionSource.ConnectionString);
            options.DisableNpgsqlLogging = true;

            configure?.Invoke(options);

            var store = new DocumentStore(options);

            _disposables.Add(store);

            return store;
        }

        protected DocumentStore StoreOptions(Action<StoreOptions> configure, bool cleanAll = true)
        {
            var options = new StoreOptions();
            options.Connection(ConnectionSource.ConnectionString);

            // Can be overridden
            options.AutoCreateSchemaObjects = AutoCreate.All;
            options.NameDataLength = 100;
            options.DatabaseSchemaName = _schemaName;

            configure(options);

            if (cleanAll)
            {
                using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
                conn.Open();
                conn.CreateCommand($"drop schema if exists {_schemaName} cascade")
                    .ExecuteNonQuery();
            }

            _store = new DocumentStore(options);

            _disposables.Add(_store);

            return _store;
        }

        protected DocumentStore theStore
        {
            get
            {
                if (_store == null)
                {
                    StoreOptions(_ => { });
                }

                return _store;
            }
            set
            {
                _store = value;
            }
        }

        protected Func<IDocumentStore, ISessionFactory> theSessionFactoryThunk =
            store => new LightweightSessionFactory(store);
        protected IDocumentSession theSession
        {
            get
            {

                if (_session != null)
                    return _session;

                _session = theSessionFactoryThunk(theStore).OpenSession();
                _disposables.Add(_session);

                return _session;
            }
        }

        public void Dispose()
        {
            foreach (var disposable in _disposables)
            {
                disposable.Dispose();
            }
        }

        protected Task AppendEvent(Guid streamId, params object[] events)
        {
            theSession.Events.Append(streamId, events);
            return theSession.SaveChangesAsync();
        }
    }
}
