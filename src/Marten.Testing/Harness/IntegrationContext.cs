using System;
using Marten.Testing.Examples;
using Xunit;

namespace Marten.Testing.Harness
{
    [CollectionDefinition("integration")]
    public class IntegrationCollection : ICollectionFixture<DefaultStoreFixture>
    {

    }

    [Collection("integration")]
    public class IntegrationContext : StoreContext<DefaultStoreFixture>
    {
        private IDocumentSession _session;
        private DocumentStore _store;

        public IntegrationContext(DefaultStoreFixture fixture) : base(fixture)
        {

        }

        /// <summary>
        /// Customize the store configuration for one off tests.
        /// The return value is the database schema
        /// </summary>
        /// <param name="configure"></param>
        /// <returns></returns>
        protected string StoreOptions(Action<StoreOptions> configure)
        {
            _overrodeStore = true;

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

        private bool _hasBuiltStore = false;
        private bool _overrodeStore;

        protected override DocumentStore theStore
        {
            get
            {
                if (_store != null) return _store;

                if (!_hasBuiltStore)
                {
                    base.theStore.Advanced.Clean.DeleteAllDocuments();
                    _hasBuiltStore = true;
                }

                return base.theStore;
            }
        }

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
            if (_overrodeStore)
            {
                Fixture.Store.Advanced.Clean.CompletelyRemoveAll();
            }

            _session?.Dispose();
            _store?.Dispose();
            base.Dispose();
        }
    }
}
