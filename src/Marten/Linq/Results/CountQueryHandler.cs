using System;
using System.Data.Common;
using Marten.Schema;
using Marten.Services;
using Marten.Util;
using Npgsql;
using Remotion.Linq;

namespace Marten.Linq.Results
{
    public class CountQueryHandler<T> : IQueryHandler<long>
    {
        private readonly QueryModel _query;
        private readonly IDocumentSchema _schema;

        public CountQueryHandler(QueryModel query, IDocumentSchema schema)
        {
            _query = query;
            _schema = schema;
        }

        public Type SourceType => _query.SourceType();

        public void ConfigureCommand(NpgsqlCommand command)
        {
            var mapping = _schema.MappingFor(_query.SourceType());

            var sql = "select count(*) as number from " + mapping.Table.QualifiedName + " as d";

            var where = _schema.BuildWhereFragment(mapping, _query);

            if (@where != null) sql += " where " + @where.ToSql(command);

            command.AppendQuery(sql);
        }

        public long Handle(DbDataReader reader, IIdentityMap map)
        {
            var hasNext = reader.Read();
            return hasNext ? reader.GetInt64(0) : 0;
        }
    }
}