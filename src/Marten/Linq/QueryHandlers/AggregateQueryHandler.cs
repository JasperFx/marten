using System;
using System.Data.Common;
using Baseline;
using Marten.Schema;
using Marten.Services;
using Marten.Util;
using Npgsql;
using Remotion.Linq;

namespace Marten.Linq.QueryHandlers
{
    public class AggregateQueryHandler<T> : IQueryHandler<T>
    {
        private readonly string _operator;
        private readonly IDocumentSchema _schema;
        private readonly QueryModel _query;

        public static AggregateQueryHandler<T> Min(IDocumentSchema schema, QueryModel query)
        {
            return new AggregateQueryHandler<T>("min({0})", schema, query);
        }

        public static AggregateQueryHandler<T> Max(IDocumentSchema schema, QueryModel query)
        {
            return new AggregateQueryHandler<T>("max({0})", schema, query);
        }

        public static AggregateQueryHandler<T> Sum(IDocumentSchema schema, QueryModel query)
        {
            return new AggregateQueryHandler<T>("sum({0})", schema, query);
        }

        public static AggregateQueryHandler<double> Average(IDocumentSchema schema, QueryModel query)
        {
            return new AggregateQueryHandler<double>("avg({0})", schema, query);
        }

        public AggregateQueryHandler(string @operator, IDocumentSchema schema, QueryModel query)
        {
            _operator = @operator;
            _schema = schema;
            _query = query;
        }

        public Type SourceType => _query.SourceType();

        public void ConfigureCommand(NpgsqlCommand command)
        {
            var mapping = _schema.MappingFor(_query.SourceType());
            var locator = mapping.JsonLocator(_query.SelectClause.Selector);
            var field = _operator.ToFormat(locator);

            var sql = $"select {field} from {mapping.Table.QualifiedName} as d";


            var @where = _schema.BuildWhereFragment(mapping, _query);
            // TODO -- this pattern is duplicated a lot
            if (@where != null) sql += " where " + @where.ToSql(command);

            command.AppendQuery(sql);

        }

        public T Handle(DbDataReader reader, IIdentityMap map)
        {
            var selector = new ScalarSelector<T>();

            return reader.Read() ? selector.Resolve(reader, map) : default(T);
        }
    }
}