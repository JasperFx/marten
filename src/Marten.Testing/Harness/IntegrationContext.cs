using System;
using Marten.Testing.Examples;
using Xunit;

namespace Marten.Testing.Harness
{
    [Collection("integration")]
    public class IntegrationContext: StoreContext<DefaultStoreFixture>
    {
        private IDocumentSession _session;
        private DocumentStore _store;

        public IntegrationContext(DefaultStoreFixture fixture) : base(fixture)
        {
            theStore.Advanced.Clean.DeleteAllDocuments();
        }

        /// <summary>
        /// Customize the store configuration for one off tests.
        /// The return value is the database schema
        /// </summary>
        /// <param name="configure"></param>
        /// <returns></returns>
        protected string StoreOptions(Action<StoreOptions> configure)
        {
            _session?.Dispose();
            _session = null;

            var options = new StoreOptions();
            options.Connection(ConnectionSource.ConnectionString);

            // Can be overridden
            options.AutoCreateSchemaObjects = AutoCreate.All;
            options.NameDataLength = 100;
            options.DatabaseSchemaName = "special";

            configure(options);

            _store = new DocumentStore(options);

            _store.Advanced.Clean.CompletelyRemoveAll();

            return options.DatabaseSchemaName;
        }

        protected override DocumentStore theStore => _store ?? base.theStore;

        protected virtual IDocumentSession theSession
        {
            get
            {
                if (_session == null)
                {
                    _session = theStore.LightweightSession();
                }

                return _session;
            }
        }

        public override void Dispose()
        {
            _session?.Dispose();
            _store?.Dispose();
            base.Dispose();
        }
    }
}
