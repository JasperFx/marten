using System;
using StructureMap;

namespace Marten.Testing
{
    public abstract class DocumentSessionFixture : IDisposable
    {
        protected readonly IContainer theContainer = Container.For<DevelopmentModeRegistry>();
        protected readonly IDocumentSession theSession;

        protected DocumentSessionFixture()
        {
            ConnectionSource.CleanBasicDocuments();
            theSession = theContainer.GetInstance<IDocumentSession>();            
        }

        public void Dispose()
        {
            theSession.Dispose();
        }
    }
}