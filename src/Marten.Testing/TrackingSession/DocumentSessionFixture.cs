using System;
using StructureMap;

namespace Marten.Testing.TrackingSession
{
    public abstract class TrackingSessionFixture : IDisposable
    {
        protected readonly IContainer theContainer = Container.For<DevelopmentModeRegistry>();
        protected readonly ITrackingSession theSession;

        protected TrackingSessionFixture()
        {
            ConnectionSource.CleanBasicDocuments();
            theSession = CreateSession();            
        }

        protected ITrackingSession CreateSession()
        {
            return theContainer.GetInstance<ITrackingSession>();
        }

        public void Dispose()
        {
            theSession.Dispose();
        }
    }
}