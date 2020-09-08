using Marten.Internal.Operations;
using Marten.Internal.Storage;
using Marten.Schema;
using Marten.Util;

namespace Marten.Linq.SqlGeneration
{
    public class SoftDelete: IOperationFragment
    {
        private readonly string _sql;

        public SoftDelete(IDocumentStorage storage)
        {
            _sql =
                $"update {storage.QueryableDocument.Table.QualifiedName} as d set {DocumentMapping.DeletedColumn} = True, {DocumentMapping.DeletedAtColumn} = now()";
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
