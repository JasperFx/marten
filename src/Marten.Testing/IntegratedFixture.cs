using System;
using System.Globalization;
using System.Reflection;
using System.Threading;
using Baseline;
using Xunit;

namespace Marten.Testing
{
    public abstract class IntegratedFixture : IDisposable
    {
        private Lazy<IDocumentStore> _store;
        private CultureInfo _originalCulture;

        protected IntegratedFixture()
        {
            _store = new Lazy<IDocumentStore>(TestingDocumentStore.Basic);

            if (GetType().GetTypeInfo().GetCustomAttribute<CollectionAttribute>(true) != null)
            {
                UseDefaultSchema();
            }

#if NET46
            _originalCulture = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
#endif
        }

        protected string toJson<T>(T doc)
        {
            return theStore.Advanced.Options.Serializer().ToJson(doc);
        }

        protected DocumentStore theStore => _store.Value.As<DocumentStore>();

        protected void UseDefaultSchema()
        {
            _store = new Lazy<IDocumentStore>(TestingDocumentStore.DefaultSchema);
        }

        protected void StoreOptions(Action<StoreOptions> configure)
        {
            _store = new Lazy<IDocumentStore>(() => TestingDocumentStore.For(configure));
        }

        public virtual void Dispose()
        {
            if (_store.IsValueCreated)
            {
                _store.Value.Dispose();
            }
#if NET46
            Thread.CurrentThread.CurrentCulture = _originalCulture;
            Thread.CurrentThread.CurrentUICulture = _originalCulture;
#endif
        }
    }
}