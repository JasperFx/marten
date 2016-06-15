using System;
using System.Text;
using Marten.Linq;
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

    public class DeleteWhere : IDeletion
    {
        private readonly TableName _table;
        private readonly IWhereFragment _where;
        public Type DocumentType { get; set; }

        public DeleteWhere(Type documentType, TableName table, IWhereFragment @where)
        {
            _table = table;
            _where = @where;
            DocumentType = documentType;
        }

        void ICall.WriteToSql(StringBuilder builder)
        {
            builder.Append(Sql);
        }

        void IStorageOperation.AddParameters(IBatchCommand batch)
        {
            var whereClause = _where.ToSql(batch.Command);
            Sql = $"delete from {_table.QualifiedName} as d where {whereClause}";
        }

        public string Sql { get; private set; }

        public override string ToString()
        {
            return $"Delete {DocumentType}: {Sql}";
        }
    }
}