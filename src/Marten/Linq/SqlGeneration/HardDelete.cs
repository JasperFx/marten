using Marten.Internal.Operations;
using Marten.Internal.Storage;
using Marten.Util;

namespace Marten.Linq.SqlGeneration
{
    public class HardDelete: IOperationFragment
    {
        private readonly string _sql;

        public HardDelete(IDocumentStorage storage)
        {
            _sql = $"delete from {storage.QueryableDocument.Table} as d";
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
