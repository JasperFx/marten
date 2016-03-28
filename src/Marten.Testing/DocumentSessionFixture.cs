using System;
using System.Diagnostics;
using Marten.Services;
using StructureMap;

namespace Marten.Testing
{

    public abstract class IntegratedFixture : IDisposable
    {
        protected readonly IContainer theContainer;
        protected readonly IDocumentStore theStore;

        protected IntegratedFixture()
        {
            theContainer = new Container(new DevelopmentModeRegistry(StoreOptions));

            theStore = theContainer.GetInstance<IDocumentStore>();
            theStore.Advanced.Clean.CompletelyRemoveAll();
        }

        protected virtual void StoreOptions(StoreOptions options)
        {
        }

        public virtual void Dispose()
        {
            Debug.WriteLine("DISPOSING!");
            theStore.Dispose();
        }
    }

    public abstract class DocumentSessionFixture<T> : IntegratedFixture, IDisposable where T : IIdentityMap
    {
        protected readonly IDocumentSession theSession;
        

        protected DocumentSessionFixture()
        {
            theSession = CreateSession();            
        }

        protected IDocumentSession CreateSession()
        {
            if (typeof (T) == typeof (NulloIdentityMap))
            {
                return theStore.OpenSession(DocumentTracking.None);
            }

            if (typeof (T) == typeof (IdentityMap))
            {
                return theStore.OpenSession();
            }

            return theStore.DirtyTrackedSession();
        }

        public override void Dispose()
        {
            Debug.WriteLine("I AM BEING DISPOSED!");
            theSession.Dispose();
            theStore.Dispose();
            theContainer.Dispose();
        }
    }
}