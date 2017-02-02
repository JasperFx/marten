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
            var parts = Sql.Split('?');

            builder.Append(parts[0]);
            _where.Apply(builder);
            builder.Append(parts[1]);
        }

        public string Sql { get; }

        public override string ToString()
        {
            return $"Delete {DocumentType}: {Sql}";
        }
    }
}