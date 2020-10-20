using System;
using System.Collections;
using System.Collections.Generic;
using Marten.Testing.Examples;
using Xunit;

namespace Marten.Testing.Harness
{
    public class SessionTypes : IEnumerable<object[]>
    {
        public IEnumerator<object[]> GetEnumerator()
        {
            yield return new object[] { DocumentTracking.None};
            yield return new object[] { DocumentTracking.IdentityOnly};
            yield return new object[] { DocumentTracking.DirtyTracking};
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    /// <summary>
    /// Use to build a theory test using a separate session with each kind of document tracking
    /// </summary>
    public class SessionTypesAttribute : ClassDataAttribute
    {
        public SessionTypesAttribute() : base(typeof(SessionTypes))
        {
        }
    }

    [CollectionDefinition("integration")]
    public class IntegrationCollection : ICollectionFixture<DefaultStoreFixture>
    {

    }

    [Collection("integration")]
    public class IntegrationContext : StoreContext<DefaultStoreFixture>
    {
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

            if (_session != null)
            {
                _session.Dispose();
                Disposables.Remove(_session);
                _session = null;
            }


            var options = new StoreOptions();
            options.Connection(ConnectionSource.ConnectionString);

            // Can be overridden
            options.AutoCreateSchemaObjects = AutoCreate.All;
            options.NameDataLength = 100;
            options.DatabaseSchemaName = "special";

            configure(options);

            _store = new DocumentStore(options);
            Disposables.Add(_store);

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
                    base.theStore.Advanced.Clean.DeleteAllEventData();
                    _hasBuiltStore = true;
                }

                return base.theStore;
            }
        }

        public override void Dispose()
        {
            if (_overrodeStore)
            {
                Fixture.Store.Advanced.Clean.CompletelyRemoveAll();
            }

            base.Dispose();
        }
    }
}
