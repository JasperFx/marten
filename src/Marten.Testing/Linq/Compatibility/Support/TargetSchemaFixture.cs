using System;
using System.Collections.Generic;
using System.Linq;

namespace Marten.Testing.Linq.Compatibility.Support
{
    public abstract class TargetSchemaFixture: IDisposable
    {
        public readonly Target[] Documents = Target.GenerateRandomData(1000).ToArray();

        private readonly IList<DocumentStore> _stores = new List<DocumentStore>();

        public void Dispose()
        {
            foreach (var documentStore in _stores)
            {
                documentStore.Dispose();
            }
        }

        protected DocumentStore provisionStore(string schema, Action<StoreOptions> configure = null)
        {
            var store = DocumentStore.For(x =>
            {
                x.Connection(ConnectionSource.ConnectionString);
                x.DatabaseSchemaName = schema;

                configure?.Invoke(x);
            });

            store.Advanced.Clean.CompletelyRemoveAll();

            store.BulkInsert(Documents);

            _stores.Add(store);

            return store;
        }
    }
}
