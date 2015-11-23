using Marten.Schema;

namespace Marten
{
    public interface IDocumentStore
    {
        IDocumentSchema Schema { get; }
        AdvancedOptions Advanced { get; }


        void BulkInsert<T>(T[] documents, int batchSize = 1000);

        IDiagnostics Diagnostics { get; }
    }
}