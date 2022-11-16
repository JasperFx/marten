using Marten.Internal.Storage;
using Marten.Services;

#nullable enable

namespace Marten.Linq.SqlGeneration
{
    internal class Deletion: StatementOperation, IDeletion
    {
        public Deletion(IDocumentStorage storage, IOperationFragment operation, string? tenantId) : base(storage, operation, tenantId)
        {
        }

        public object Document { get; set; }
        public object Id { get; set; }

    }
}
