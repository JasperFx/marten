using System;
using System.Text;
using Marten.Schema;
using Marten.Util;
using Npgsql;

namespace Marten.Services.Deletes
{
    public class DeleteById : IDeletion
    {
        private readonly IDocumentStorage _storage;
        public object Id { get; }
        public object Document { get; }

        public DeleteById(string sql, IDocumentStorage storage, object id, object document = null)
        {
            Sql = sql;
            _storage = storage;
            if (id == null) throw new ArgumentNullException(nameof(id));

            Id = id;
            Document = document;

        }

        public string Sql { get; }

        public Type DocumentType => _storage.DocumentType;

        public void ConfigureCommand(CommandBuilder builder)
        {
            var param = builder.AddParameter(Id, _storage.IdType);
            builder.Append(Sql.Replace("?", ":" + param.ParameterName));
        }

        public override string ToString()
        {
            return $"Delete {DocumentType} with Id {Id}: {Sql}";
        }
    }
}