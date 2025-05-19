using System;
using JasperFx;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Testing.Harness
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
