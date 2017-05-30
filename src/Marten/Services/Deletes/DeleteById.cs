using System;
using System.Text;
using Marten.Schema;
using Marten.Schema.Arguments;
using Marten.Storage;
using Marten.Util;
using Npgsql;

namespace Marten.Services.Deletes
{

    public class DeleteById : IDeletion
    {
        private readonly TenancyStyle _tenancyStyle;
        private readonly IDocumentStorage _storage;
        public object Id { get; }
        public object Document { get; }

        public DeleteById(TenancyStyle tenancyStyle, string sql, IDocumentStorage storage, object id, object document = null)
        {
            Sql = sql;
            _tenancyStyle = tenancyStyle;
            _storage = storage;

            Id = id ?? throw new ArgumentNullException(nameof(id));
            Document = document;

        }

        public string Sql { get; }

        public Type DocumentType => _storage.DocumentType;

        public void ConfigureCommand(CommandBuilder builder)
        {
            var param = builder.AddParameter(Id, _storage.IdType);
            builder.Append(Sql.Replace("?", ":" + param.ParameterName));
            if (_tenancyStyle == TenancyStyle.Conjoined)
            {
                builder.Append($" and {TenantIdColumn.Name} = :{TenantIdArgument.ArgName}");
            }
        }

        public override string ToString()
        {

            return $"Delete {DocumentType} with Id {Id}: {Sql}";
        }
    }
}