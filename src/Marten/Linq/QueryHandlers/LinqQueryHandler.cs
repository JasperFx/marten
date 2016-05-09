using System;
using System.Linq;
using Baseline;
using Marten.Schema;
using Marten.Services.Includes;
using Marten.Util;
using Npgsql;
using Remotion.Linq;

namespace Marten.Linq.QueryHandlers
{
    public class LinqQueryHandler<T> : ListQueryHandler<T>
    {
        private readonly IDocumentSchema _schema;
        private readonly QueryModel _query;
        private readonly IDocumentMapping _mapping;

        public static ISelector<T> BuildSelector(IDocumentSchema schema, QueryModel query,
            IIncludeJoin[] joins, QueryStatistics stats)
        {
            var mapping = schema.MappingFor(query);
            var selector = schema.BuildSelector<T>(mapping, query);

            if (stats != null)
            {
                selector = new StatsSelector<T>(stats, selector);
            }

            if (joins.Any())
            {
                selector = new IncludeSelector<T>(schema, selector, joins);
            }

            return selector;
        }

        public LinqQueryHandler(IDocumentSchema schema, QueryModel query, IIncludeJoin[] joins, QueryStatistics stats)
            : base(BuildSelector(schema, query, joins, stats))
        {
            _mapping = schema.MappingFor(query);
            _schema = schema;
            _query = query;
        }


        public override Type SourceType => _query.SourceType();
        public override void ConfigureCommand(NpgsqlCommand command)
        {
            var sql = Selector.ToSelectClause(_mapping);
            var @where = _schema.BuildWhereFragment(_mapping, _query);



            sql = sql.AppendWhere(@where, command);

            var orderBy = _query.ToOrderClause(_mapping);
            if (orderBy.IsNotEmpty()) sql += orderBy;

            sql = _query.AppendLimit(sql);
            sql = _query.AppendOffset(sql);

            command.AppendQuery(sql);
        }


    }
}