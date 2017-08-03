using System;
using System.Text;
using Marten.Linq;
using Marten.Schema.Arguments;
using Marten.Storage;
using Marten.Util;

namespace Marten.Services.Deletes
{
    public class DeleteWhere : IDeletion
    {
        private readonly IWhereFragment _where;
        private readonly TenancyStyle _tenancyStyle;
        public Type DocumentType { get; set; }

        public DeleteWhere(Type documentType, string sql, IWhereFragment @where, TenancyStyle tenancyStyle)
        {
            _where = @where;
            _tenancyStyle = tenancyStyle;
            DocumentType = documentType;
            Sql = sql;
        }

        public void ConfigureCommand(CommandBuilder builder)
        {
            var parts = Sql.Split('?');

            builder.Append(parts[0]);
            _where.Apply(builder);
            builder.Append(parts[1]);

            if (_tenancyStyle == TenancyStyle.Conjoined)
            {
                builder.Append($" and {TenantWhereFragment.Filter}");
            }
        }

        public string Sql { get; }

        public override string ToString()
        {
            return $"Delete {DocumentType}: {Sql}";
        }
    }
}