using Marten.Internal.Storage;
using Marten.Schema.BulkLoading;

namespace Marten.Internal.CodeGeneration
{
    public class DocumentProvider<T>
    {
        public IDocumentStorage<T> QueryOnly { get; set; }
        public IDocumentStorage<T> Lightweight { get; set; }
        public IDocumentStorage<T> IdentityMap { get; set; }
        public IDocumentStorage<T> DirtyTracking { get; set; }
        public IBulkLoader<T> BulkLoader { get; set; }

        public string SourceCode { get; set; }
    }
}
