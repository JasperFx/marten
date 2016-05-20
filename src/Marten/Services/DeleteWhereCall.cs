using System;
using System.Text;
using Marten.Schema;

namespace Marten.Services
{
    public class DeleteWhereCall : ICall
    {
        private readonly TableName _table;
        private readonly string _whereClause;

        public DeleteWhereCall(TableName table, string whereClause)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));

            _table = table;
            _whereClause = whereClause;
        }

        public void WriteToSql(StringBuilder builder)
        {
            builder.AppendFormat("delete from {0} as d where {1}", _table, _whereClause);
        }
    }
}