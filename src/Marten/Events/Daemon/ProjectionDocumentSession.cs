using System;
using Marten.Internal.Sessions;
using Marten.Services;
using Marten.Storage;

namespace Marten.Events.Daemon
{
    internal class ProjectionDocumentSession: DocumentSessionBase
    {
        public ProjectionDocumentSession(DocumentStore store, ITenant tenant, ISessionWorkTracker workTracker): base(
            store, new SessionOptions {Tracking = DocumentTracking.None}, tenant.OpenConnection(), tenant, workTracker)
        {
        }

        protected override void ejectById<T>(long id)
        {
            // nothing
        }

        protected override void ejectById<T>(int id)
        {
            // nothing
        }

        protected override void ejectById<T>(Guid id)
        {
            // nothing
        }

        protected override void ejectById<T>(string id)
        {
            // nothing
        }
    }
}
