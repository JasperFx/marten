#nullable enable
using Marten.Internal.Operations;
using Marten.Internal.Storage;
using Marten.Schema;
using Weasel.Core.Operations;
using Weasel.Postgresql;

namespace Marten.Linq.SqlGeneration;

internal class SoftDelete: IOperationFragment
{
    private readonly string _sql;

    public SoftDelete(IDocumentStorage storage)
    {
        _sql =
            $"update {storage.TableName.QualifiedName} as d set {SchemaConstants.DeletedColumn} = True, {SchemaConstants.DeletedAtColumn} = now()";
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
