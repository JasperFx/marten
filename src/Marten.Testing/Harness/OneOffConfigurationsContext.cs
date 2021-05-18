using System;
using System.Collections.Generic;
using Baseline;
using Weasel.Postgresql;
using Xunit;

namespace Marten.Testing.Harness
{
    /// <summary>
    /// Use this if the tests in a fixture are going to use
    /// all custom StoreOptions configuration
    /// </summary>
    public abstract class OneOffConfigurationsContext : IDisposable
    {
        private readonly string _schemaName;
        private DocumentStore _store;
        private IDocumentSession _session;
        protected readonly IList<IDisposable> _disposables = new List<IDisposable>();

        public string SchemaName => _schemaName;

        protected OneOffConfigurationsContext(string schemaName)
        {
            if (!GetType().HasAttribute<CollectionAttribute>())
            {
                throw new InvalidOperationException("You must decorate this class with a [Collection(\"schemaname\"] attribute. Preferably w/ the schema name");
            }

            _schemaName = schemaName;
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
            var options = new StoreOptions
            {
                DatabaseSchemaName = SchemaName,

            };

            options.Connection(ConnectionSource.ConnectionString);

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

            _store = new DocumentStore(options);

            if (cleanAll)
            {
                _store.Advanced.Clean.CompletelyRemoveAll();
            }

            _disposables.Add(_store);

            return _store;
        }

        protected DocumentStore theStore
        {
            get
            {
                if (_store == null)
                {
                    StoreOptions(x => {});
                }

                return _store;
            }
        }

        protected virtual IDocumentSession theSession
        {
            get
            {
                if (_session == null)
                {
                    _session = theStore.LightweightSession();
                    _disposables.Add(_session);
                }

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


    }
}
