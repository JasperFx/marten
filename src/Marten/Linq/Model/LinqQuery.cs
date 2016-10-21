using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Linq.QueryHandlers;
using Marten.Schema;
using Marten.Services.Includes;
using Marten.Util;
using Npgsql;
using Remotion.Linq;
using Remotion.Linq.Clauses;

namespace Marten.Linq.Model
{
    public class LinqQuery<T>
    {
        private readonly QueryModel _query;
        private readonly IDocumentSchema _schema;
        private readonly IQueryableDocument _mapping;

        private readonly SelectManyQuery _subQuery;

        public LinqQuery(QueryModel query, IDocumentSchema schema, IIncludeJoin[] joins, QueryStatistics stats)
        {
            _query = query;
            _schema = schema;
            _mapping = schema.MappingFor(query).ToQueryableDocument();

            for (int i = 0; i < query.BodyClauses.Count; i++)
            {
                var clause = query.BodyClauses[i];
                if (clause is AdditionalFromClause)
                {
                    // TODO -- to be able to go recursive, have _subQuery start to read the BodyClauses
                    _subQuery = new SelectManyQuery(_mapping, query, i + 1);


                    break;
                }
            }

            SourceType = _query.SourceType();
            Selector = BuildSelector(schema, query, joins, stats);

            Where = buildWhereFragment();
        }

        public IWhereFragment Where { get; set; }

        public Type SourceType { get; }

        public ISelector<T> Selector { get; }

        public static ISelector<T> BuildSelector(IDocumentSchema schema, QueryModel query,
            IIncludeJoin[] joins, QueryStatistics stats)
        {
            var mapping = schema.MappingFor(query).ToQueryableDocument();
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


        public void ConfigureCommand(NpgsqlCommand command)
        {
            var sql = Selector.ToSelectClause(_mapping);

            string filter = null;
            if (Where != null)
            {
                filter = Where.ToSql(command);
            }

            if (filter.IsNotEmpty())
            {
                sql += " where " + filter;
            }

            var orderBy = _query.ToOrderClause(_mapping);
            if (orderBy.IsNotEmpty()) sql += orderBy;

            sql = _query.AppendLimit(sql);
            sql = _query.AppendOffset(sql);

            command.AppendQuery(sql);
        }

        private IWhereFragment buildWhereFragment()
        {
            var bodies = _subQuery == null 
                ? _query.AllBodyClauses() 
                : _query.BodyClauses.Take(_subQuery.Index);

            var wheres = bodies.OfType<WhereClause>().ToArray();
            if (wheres.Length == 0) return _mapping.DefaultWhereFragment();

            var where = wheres.Length == 1
                ? _schema.Parser.ParseWhereFragment(_mapping, wheres.Single().Predicate)
                : new CompoundWhereFragment(_schema.Parser, _mapping, "and", wheres);

            return _mapping.FilterDocuments(_query, where);
        }
    }
}