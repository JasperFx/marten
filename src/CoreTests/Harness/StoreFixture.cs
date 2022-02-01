using System;
using Marten;
using Marten.Testing.Harness;
using Weasel.Core;

namespace CoreTests.Harness
{
    public abstract class StoreFixture: IDisposable
    {
        /// <summary>
        /// Each StoreFixture type needs to have
        /// a unique schema name to avoid naming collisions
        /// </summary>
        /// <param name="schemaName"></param>
        protected StoreFixture(string schemaName)
        {
            Options.DatabaseSchemaName = schemaName;
            Options.Connection(ConnectionSource.ConnectionString);

            // Can be overridden
            Options.AutoCreateSchemaObjects = AutoCreate.All;
        }

        protected StoreOptions Options { get; } = new StoreOptions();

        private DocumentStore _store;

        public DocumentStore Store
        {
            get
            {
                if (_store == null)
                {
                    _store = new DocumentStore(Options);
                    _store.Advanced.Clean.CompletelyRemoveAll();
                }

                return _store;
            }
        }

        public void Dispose()
        {

            _store?.Dispose();
        }
    }
}
