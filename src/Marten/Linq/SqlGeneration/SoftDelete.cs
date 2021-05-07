using Marten.Internal.Operations;
using Marten.Internal.Storage;
using Weasel.Postgresql;
using Marten.Schema;
using Marten.Util;

namespace Marten.Linq.SqlGeneration
{
    internal class SoftDelete: IOperationFragment
    {
        private readonly string _sql;

        public SoftDelete(IDocumentStorage storage)
        {
            _sql =
                $"update {storage.TableName.QualifiedName} as d set {SchemaConstants.DeletedColumn} = True, {SchemaConstants.DeletedAtColumn} = now()";
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
