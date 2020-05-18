using System;
using Marten.Services;
using Marten.Testing.Harness;
using Xunit;
using Xunit.Abstractions;

namespace Marten.NodaTime.Testing
{
    public abstract class IntegrationContext : IDisposable
    {
        protected ITestOutputHelper _output;
        private DocumentStore _store;

        private IDocumentSession _session;

#if NET461
        private CultureInfo _originalCulture;
#endif

        protected IntegrationContext(ITestOutputHelper output = null)
        {
            _output = output;

            UseDefaultSchema();
            _store.Advanced.Clean.CompletelyRemoveAll();

#if NET461
            _originalCulture = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
#endif
        }

        protected bool EnableCommandLogging { get; set; }


        protected DocumentStore theStore
        {
            get
            {
                if (_store == null)
                {
                    UseDefaultSchema();
                }

                return _store;
            }
        }

        protected void UseDefaultSchema()
        {
            StoreOptions(x => x.DatabaseSchemaName = "public");
        }

        protected DocumentStore StoreOptions(Action<StoreOptions> configure)
        {
            _session?.Dispose();
            _session = null;
            _store = DocumentStore.For(opts =>
            {
                opts.NameDataLength = 100;
                opts.Connection(ConnectionSource.ConnectionString);
                if (EnableCommandLogging)
                {
                    opts.Logger(new TestOutputMartenLogger(_output));
                }

                opts.AutoCreateSchemaObjects = AutoCreate.All;

                configure(opts);
            });

            _store.Advanced.Clean.CompletelyRemoveAll();

            return _store;
        }

        /// <summary>
        /// This creates an all new DocumentStore without
        /// cleaning the schema
        /// </summary>
        /// <param name="configure"></param>
        /// <returns></returns>
        protected DocumentStore SeparateStore(Action<StoreOptions> configure)
        {

            var store = DocumentStore.For(opts =>
            {
                opts.NameDataLength = 100;
                opts.Connection(ConnectionSource.ConnectionString);
                if (EnableCommandLogging)
                {
                    opts.Logger(new TestOutputMartenLogger(_output));
                }

                opts.AutoCreateSchemaObjects = AutoCreate.All;

                configure(opts);
            });


            return store;
        }

        protected IDocumentSession theSession
        {
            get
            {
                if (_session == null)
                {
                    _session = buildSession();
                }

                return _session;
            }
        }

        protected virtual IDocumentSession buildSession()
        {
            return theStore.LightweightSession();
        }

        public virtual void Dispose()
        {
            _store?.Dispose();
#if NET461
            Thread.CurrentThread.CurrentCulture = _originalCulture;
            Thread.CurrentThread.CurrentUICulture = _originalCulture;
#endif
        }
    }

    public abstract class IntegrationContextWithIdentityMap<T>: IntegrationContext where T : IIdentityMap
    {
        protected override IDocumentSession buildSession()
        {
            if (typeof(T) == typeof(NulloIdentityMap))
            {
                return theStore.OpenSession(tracking:DocumentTracking.None);
            }

            if (typeof(T) == typeof(IdentityMap))
            {
                return theStore.OpenSession();
            }

            return theStore.DirtyTrackedSession();
        }
    }
}
