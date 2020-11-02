using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Baseline;
using Marten.Services;
using Marten.Testing.Harness;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Schema.Testing
{


    public abstract class IntegrationContext : IDisposable
    {
        protected ITestOutputHelper _output;
        private DocumentStore _store;

        private IDocumentSession _session;

        protected IntegrationContext(ITestOutputHelper output = null)
        {
            _output = output;

            UseDefaultSchema();
            _store.Advanced.Clean.CompletelyRemoveAll();
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

        public DocumentTracking DocumentTracking { get; set; } = DocumentTracking.None;

        protected IDocumentSession buildSession()
        {
            return theStore.OpenSession(DocumentTracking);
        }

        public virtual void Dispose()
        {
            _store?.Dispose();
        }
    }


}
