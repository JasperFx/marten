using System;
using Marten.Internal.CodeGeneration;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Services;
using Marten.Storage;

namespace Marten.Events.Daemon
{
    /// <summary>
    /// Lightweight session specifically used to capture operations for a specific tenant
    /// in the asynchronous projections
    /// </summary>
    internal class ProjectionDocumentSession: DocumentSessionBase
    {
        public ProjectionDocumentSession(DocumentStore store, ITenant tenant, ISessionWorkTracker workTracker): base(
            store, new SessionOptions {Tracking = DocumentTracking.None}, tenant.OpenConnection(), tenant, workTracker)
        {
        }

        protected internal override IDocumentStorage<T> selectStorage<T>(DocumentProvider<T> provider)
        {
            return provider.Lightweight;
        }

        protected internal override void ejectById<T>(long id)
        {
            // nothing
        }

        protected internal override void ejectById<T>(int id)
        {
            // nothing
        }

        protected internal override void ejectById<T>(Guid id)
        {
            // nothing
        }

        protected internal override void ejectById<T>(string id)
        {
            // nothing
        }
    }
}
