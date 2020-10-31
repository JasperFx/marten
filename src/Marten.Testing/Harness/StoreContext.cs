using System;
using System.Collections.Generic;
using Xunit;

namespace Marten.Testing.Harness
{
    public abstract class StoreContext<T> : IDisposable where T : StoreFixture
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
                    _session = theStore.OpenSession(DocumentTracking);
                    Disposables.Add(_session);
                }

                return _session;
            }
        }
    }
}
