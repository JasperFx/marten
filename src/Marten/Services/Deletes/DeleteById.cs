using System;
using System.Text;
using Marten.Schema;
using Npgsql;

namespace Marten.Services.Deletes
{
    public class DeleteById : IDeletion
    {
        private readonly IQueryableDocument _queryable;
        private readonly IDocumentStorage _storage;
        private NpgsqlParameter _param;
        public object Id { get; }
        public object Document { get; }

        public DeleteById(IQueryableDocument queryable, IDocumentStorage storage, object id, object document = null)
        {
            _queryable = queryable;
            _storage = storage;
            if (id == null) throw new ArgumentNullException(nameof(id));

            Id = id;
            Document = document;

        }

        public Type DocumentType => _storage.DocumentType;

        void ICall.WriteToSql(StringBuilder builder)
        {
            builder.AppendFormat("delete from {0} where id = :{1}", _queryable.Table.QualifiedName, _param.ParameterName);
        }

        void IStorageOperation.AddParameters(IBatchCommand batch)
        {
            _param = batch.AddParameter(Id, _storage.IdType);
        }

        public override string ToString()
        {
            return $"Delete {DocumentType} with Id {Id}";
        }
    }
}