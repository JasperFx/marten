using System;
using Marten.Schema;

namespace Marten
{
    public interface IDocumentStore : IDisposable
    {
        IDocumentSchema Schema { get; }
        AdvancedOptions Advanced { get; }


        void BulkInsert<T>(T[] documents, int batchSize = 1000);

        IDiagnostics Diagnostics { get; }

        IDocumentSession OpenSession(DocumentTracking tracking = DocumentTracking.IdentityOnly);

        IDocumentSession LightweightSession();

        IDocumentSession DirtyTrackedSession();

        IQuerySession QuerySession();
    }

    public enum DocumentTracking
    {
        None,
        IdentityOnly,
        DirtyTracking
    }
}