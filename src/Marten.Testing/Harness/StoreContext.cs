using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Services;
using Xunit;

namespace Marten.Testing.Harness
{
    public abstract class StoreContext<T> : IDisposable, IAsyncLifetime where T : StoreFixture
    {
        protected StoreContext(T fixture)
        {
            Fixture = fixture;
        }

        public virtual void Dispose()
        {
            foreach (var disposable in Disposables)
            {
                disposable.Dispose();
            }
        }

        /// <remarks>
        /// See OneOffConfigurationsContext for why this base owns the async lifetime pair:
        /// under xunit v3 a derived IAsyncLifetime suppresses the runner's Dispose() call,
        /// so the async teardown has to chain here.
        /// </remarks>
        public virtual ValueTask InitializeAsync() => default;

        public virtual ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }

        /// <summary>
        /// Sets the default DocumentTracking for this context. Default is "None"
        /// </summary>
        protected DocumentTracking DocumentTracking { get; set; } = DocumentTracking.None;


        protected virtual DocumentStore theStore => Fixture.Store;

        protected T Fixture { get; }

        protected IDocumentSession _session;
        protected readonly IList<IDisposable> Disposables = new List<IDisposable>();

        protected IDocumentSession theSession
        {
            get
            {
                if (_session == null)
                {
                    var options = new SessionOptions { Tracking = DocumentTracking };
                    _session = theStore.OpenSession(options);
                    Disposables.Add(_session);
                }

                return _session;
            }
        }

        protected async Task AppendEvent(Guid streamId, params object[] events)
        {
            theSession.Events.Append(streamId, events);
            await theSession.SaveChangesAsync();
        }
    }
}
