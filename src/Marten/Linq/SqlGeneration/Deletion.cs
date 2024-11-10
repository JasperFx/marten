#nullable enable
using Marten.Internal.Storage;
using Marten.Services;
using Weasel.Core.Operations;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration;

internal class Deletion: StatementOperation, IDeletion
{
    public Deletion(IDocumentStorage storage, IOperationFragment operation, ISqlFragment where): base(storage,
        operation, where)
    {
    }

    public Deletion(IDocumentStorage storage, IOperationFragment operation): base(storage, operation)
    {
    }

    public object Document { get; set; } = null!;
    public object Id { get; set; } = null!;
}
