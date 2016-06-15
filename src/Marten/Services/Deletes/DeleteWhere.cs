using System;
using System.Text;
using Marten.Linq;
using Marten.Schema;

namespace Marten.Services.Deletes
{
    public class DeleteWhere : IDeletion
    {
        private readonly TableName _table;
        private readonly IWhereFragment _where;
        public Type DocumentType { get; set; }

        public DeleteWhere(Type documentType, string sql, IWhereFragment @where)
        {
            _where = @where;
            DocumentType = documentType;
            Sql = sql;
        }

        void ICall.WriteToSql(StringBuilder builder)
        {
            builder.Append(Sql);
        }

        void IStorageOperation.AddParameters(IBatchCommand batch)
        {
            var whereClause = _where.ToSql(batch.Command);

            Sql = Sql.Replace("?", whereClause);
        }

        public string Sql { get; private set; }

        public override string ToString()
        {
            return $"Delete {DocumentType}: {Sql}";
        }
    }
}