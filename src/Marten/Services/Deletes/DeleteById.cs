using System;
using System.Text;
using Marten.Schema;
using Npgsql;

namespace Marten.Services.Deletes
{
    public class DeleteById : IDeletion
    {
        private readonly IDocumentStorage _storage;
        private NpgsqlParameter _param;
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

        void ICall.WriteToSql(StringBuilder builder)
        {
            builder.Append(Sql.Replace("?", ":" + _param.ParameterName));
        }

        void IStorageOperation.AddParameters(IBatchCommand batch)
        {
            _param = batch.AddParameter(Id, _storage.IdType);
        }

        public override string ToString()
        {
            return $"Delete {DocumentType} with Id {Id}: {Sql}";
        }
    }
}