#nullable enable
using Marten.Internal.Operations;
using Marten.Internal.Storage;
using Weasel.Core.Operations;
using Weasel.Postgresql;

namespace Marten.Linq.SqlGeneration;

internal class HardDelete: IOperationFragment
{
    private readonly string _sql;

    public HardDelete(IDocumentStorage storage)
    {
        _sql = $"delete from {storage.TableName} as d";
    }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append(_sql);
    }

    public OperationRole Role()
    {
        return OperationRole.Deletion;
    }
}
