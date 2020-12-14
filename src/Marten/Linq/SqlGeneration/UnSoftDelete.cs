using Marten.Internal.Operations;
using Marten.Internal.Storage;
using Marten.Schema;
using Marten.Util;

namespace Marten.Linq.SqlGeneration
{
    internal class UnSoftDelete: IOperationFragment
    {
        private readonly string _sql;

        public UnSoftDelete(IDocumentStorage storage)
        {
            _sql =
                $"update {storage.TableName.QualifiedName} as d set {SchemaConstants.DeletedColumn} = False, {SchemaConstants.DeletedAtColumn} = NULL";
        }

        public void Apply(CommandBuilder builder)
        {
            builder.Append(_sql);
        }

        public bool Contains(string sqlText)
        {
            return _sql.Contains(sqlText);
        }

        public OperationRole Role()
        {
            return OperationRole.Deletion;
        }
    }
}
