using System;
using System.Text;
using Marten.Linq;
using Marten.Util;

namespace Marten.Services.Deletes
{
    public class DeleteWhere : IDeletion
    {
        private readonly IWhereFragment _where;
        public Type DocumentType { get; set; }

        public DeleteWhere(Type documentType, string sql, IWhereFragment @where)
        {
            _where = @where;
            DocumentType = documentType;
            Sql = sql;
        }

        public void ConfigureCommand(CommandBuilder builder)
        {
            var whereClause = _where.ToSql(builder);

            builder.Append(Sql.Replace("?", whereClause));
        }

        public string Sql { get; }

        public override string ToString()
        {
            return $"Delete {DocumentType}: {Sql}";
        }
    }
}