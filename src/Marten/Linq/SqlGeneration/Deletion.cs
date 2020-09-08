using Marten.Internal.Storage;
using Marten.Services;

namespace Marten.Linq.SqlGeneration
{
    public class Deletion: StatementOperation, IDeletion
    {
        public Deletion(IDocumentStorage storage) : base(storage, storage.DeleteFragment)
        {

        }

        public object Document { get; set; }
        public object Id { get; set; }

    }
}
