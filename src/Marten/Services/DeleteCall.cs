using System;
using System.Text;
using Marten.Schema;

namespace Marten.Services
{
    [Obsolete]
    public class DeleteCall : ICall
    {
        private readonly TableName _table;
        private readonly string _idParam;

        public DeleteCall(TableName table, string idParam)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));

            _table = table;
            _idParam = idParam;
        }


        public void WriteToSql(StringBuilder builder)
        {
            builder.AppendFormat("delete from {0} where id=:{1}", _table.QualifiedName, _idParam);
        }
    }
}