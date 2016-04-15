using System;
using System.Data.Common;
using Marten.Schema;
using Marten.Services;
using Marten.Util;
using Npgsql;
using Remotion.Linq;

namespace Marten.Linq.QueryHandlers
{
    public class AnyQueryHandler : IQueryHandler<bool>
    {
        private readonly QueryModel _query;
        private readonly IDocumentSchema _schema;

        public AnyQueryHandler(QueryModel query, IDocumentSchema schema)
        {
            _query = query;
            _schema = schema;
        }

        public Type SourceType => _query.SourceType();

        public void ConfigureCommand(NpgsqlCommand command)
        {
            var mapping = _schema.MappingFor(_query);
            var sql = "select (count(*) > 0) as result from " + mapping.Table.QualifiedName + " as d";

            var where = _schema.BuildWhereFragment(mapping, _query);
            if (@where != null) sql += " where " + @where.ToSql(command);

            command.AppendQuery(sql);
        }

        public bool Handle(DbDataReader reader, IIdentityMap map)
        {
            reader.Read();

            return reader.GetBoolean(0);
        }
    }
}