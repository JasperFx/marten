using System;
using Marten.Services;
using StructureMap;

namespace Marten.Testing
{

    public abstract class IntegratedFixture
    {
        protected readonly IContainer theContainer = Container.For<DevelopmentModeRegistry>();
        protected readonly IDocumentStore theStore;

        protected IntegratedFixture()
        {
            ConnectionSource.CleanBasicDocuments();

            theStore = theContainer.GetInstance<IDocumentStore>();
        }
    }

    public abstract class DocumentSessionFixture<T> : IntegratedFixture, IDisposable where T : IIdentityMap
    {
        protected readonly IDocumentSession theSession;
        

        protected DocumentSessionFixture()
        {
            ConnectionSource.CleanBasicDocuments();
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

        public void Dispose()
        {
            theSession.Dispose();
        }
    }
}