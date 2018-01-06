using System;
using System.Globalization;
using System.Reflection;
using System.Threading;
using Baseline;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing
{
    public abstract class IntegratedFixture : IDisposable
    {
        protected ITestOutputHelper _output;
        private Lazy<IDocumentStore> _store;
#if NET46
        private CultureInfo _originalCulture;
#endif

        protected IntegratedFixture(ITestOutputHelper output = null)
        {
            _output = output;
            _store = new Lazy<IDocumentStore>(() => TestingDocumentStore.Basic(EnableCommandLogging ? _output : null));

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

        protected bool EnableCommandLogging { get; set; }

        protected string toJson<T>(T doc)
        {
            return theStore.Options.Serializer().ToJson(doc);
        }

        protected DocumentStore theStore => _store.Value.As<DocumentStore>();

        protected void UseDefaultSchema()
        {
            _store = new Lazy<IDocumentStore>(() => TestingDocumentStore.DefaultSchema(EnableCommandLogging ? _output : null));
        }

        protected void StoreOptions(Action<StoreOptions> configure)
        {
            _store = new Lazy<IDocumentStore>(() => TestingDocumentStore.For(_ =>
            {
                if (EnableCommandLogging)
                    _.Logger(new TestOutputMartenLogger(_output));
                configure(_);
            }));
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