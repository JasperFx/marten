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
            var map = theContainer.GetInstance<T>();

            return theContainer.With<IIdentityMap>(map).GetInstance<IDocumentSession>();
        }

        public void Dispose()
        {
            theSession.Dispose();
        }
    }
}