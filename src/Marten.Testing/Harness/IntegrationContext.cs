using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.CodeGeneration;
using JasperFx.Core.Reflection;
using Marten.Events;
using Marten.Internal.CodeGeneration;
using Microsoft.FSharp.Core;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace Marten.Testing.Harness
{
    public class SessionTypes: IEnumerable<object[]>
    {
        public IEnumerator<object[]> GetEnumerator()
        {
            yield return new object[] { DocumentTracking.None };
            yield return new object[] { DocumentTracking.IdentityOnly };
            yield return new object[] { DocumentTracking.DirtyTracking };
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    /// <summary>
    /// Use to build a theory test using a separate session with each kind of document tracking
    /// </summary>
    public class SessionTypesAttribute: ClassDataAttribute
    {
        public SessionTypesAttribute(): base(typeof(SessionTypes))
        {
        }
    }

    [CollectionDefinition("integration")]
    public class IntegrationCollection: ICollectionFixture<DefaultStoreFixture>
    {
    }

    [Collection("integration")]
    public class IntegrationContext: IDisposable, IAsyncLifetime
    {
        private readonly DefaultStoreFixture _fixture;
        private DocumentStore _store;
        private IDocumentSession _session;
        protected readonly IList<IDisposable> Disposables = new List<IDisposable>();


        public IntegrationContext(DefaultStoreFixture fixture)
        {
            _fixture = fixture;
        }

        /// <summary>
        /// Build a unique document store with the same configuration as the basic,
        /// Guid-identified store from IntegrationContext
        /// </summary>
        /// <returns></returns>
        protected IDocumentStore SeparateStore()
        {
            return DocumentStore.For(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
                opts.AutoCreateSchemaObjects = AutoCreate.All;

                opts.GeneratedCodeMode = TypeLoadMode.Auto;
                opts.ApplicationAssembly = GetType().Assembly;
            });
        }

        /// <summary>
        /// Switch the DocumentStore between stream identity styles, but reuse
        /// the underlying document store
        /// </summary>
        /// <param name="identity"></param>
        internal void UseStreamIdentity(StreamIdentity identity)
        {
            _session = null;

            if (identity == StreamIdentity.AsGuid)
            {
                _store = _fixture.Store;
            }
            else
            {
                _store = _fixture.StringStreamIdentifiers.Value;
                _store.Advanced.Clean.DeleteAllEventData();
            }
        }


        /// <summary>
        /// Customize the store configuration for one off tests.
        /// The return value is the database schema
        /// </summary>
        /// <param name="configure"></param>
        /// <returns></returns>
        protected string StoreOptions(Action<StoreOptions> configure)
        {
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
            options.DatabaseSchemaName = GetType().Name.Sanitize();

            options.DisableNpgsqlLogging = true;

            configure(options);

            _store = new DocumentStore(options);
            Disposables.Add(_store);

            _store.Advanced.Clean.CompletelyRemoveAll();

            return options.DatabaseSchemaName;
        }

        protected DocumentStore theStore
        {
            get
            {
                if (_store != null) return _store;

                return _fixture.Store;
            }
        }

        protected IDocumentSession theSession
        {
            get
            {
                if (_session == null)
                {
                    _session = theStore.LightweightSession();
                    Disposables.Add(_session);
                }

                return _session;
            }
        }

        protected IDocumentSession OpenSession(DocumentTracking tracking) =>
            tracking switch
            {
                DocumentTracking.None => theStore.LightweightSession(),
                DocumentTracking.IdentityOnly => theStore.IdentitySession(),
                DocumentTracking.DirtyTracking => theStore.DirtyTrackedSession(),
                _ => throw new ArgumentOutOfRangeException(nameof(tracking), tracking, null)
            };

        protected async Task AppendEvent(Guid streamId, params object[] events)
        {
            theSession.Events.Append(streamId, events);
            await theSession.SaveChangesAsync();
        }

        public virtual void Dispose()
        {
            foreach (var disposable in Disposables)
            {
                disposable.Dispose();
            }
        }

        public async Task InitializeAsync()
        {
            await _fixture.Store.Advanced.Clean.DeleteAllDocumentsAsync();
            await _fixture.Store.Advanced.Clean.DeleteAllEventDataAsync();

            await fixtureSetup();
        }

        protected virtual Task fixtureSetup()
        {
            return Task.CompletedTask;
        }

        public Task DisposeAsync()
        {
            Dispose();
            return Task.CompletedTask;
        }
    }
}
