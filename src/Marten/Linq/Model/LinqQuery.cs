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
using Remotion.Linq.Clauses.ResultOperators;

namespace Marten.Linq.Model
{
    public class LinqQuery<T>
    {
        private readonly IQueryableDocument _mapping;
        private readonly IDocumentSchema _schema;

        private readonly SelectManyQuery _subQuery;

        public LinqQuery(IDocumentSchema schema, QueryModel model, IIncludeJoin[] joins, QueryStatistics stats)
        {
            Model = model;
            _schema = schema;
            _mapping = schema.MappingFor(model).ToQueryableDocument();

            for (var i = 0; i < model.BodyClauses.Count; i++)
            {
                var clause = model.BodyClauses[i];
                if (clause is AdditionalFromClause)
                {
                    // TODO -- to be able to go recursive, have _subQuery start to read the BodyClauses
                    _subQuery = new SelectManyQuery(_mapping, model, i + 1);


                    break;
                }
            }

            Selector = schema.HandlerFactory.BuildSelector<T>(model, joins, stats);
            SourceType = Model.SourceType();

            Where = buildWhereFragment();
        }

        public QueryModel Model { get; }

        public ISelector<T> Selector { get; }

        public IWhereFragment Where { get; set; }

        public Type SourceType { get; }


        public IQueryHandler<IList<T>> ToList()
        {
            return new ListQueryHandler<T>(this);
        }

        public void ConfigureCommand(NpgsqlCommand command)
        {
            ConfigureCommand(command, 0);
        }

        public void ConfigureCommand(NpgsqlCommand command, int limit)
        {
            var sql = Selector.ToSelectClause(_mapping);

            sql = AppendWhere(command, sql);

            var orderBy = determineOrderClause();

            if (orderBy.IsNotEmpty()) sql += orderBy;

            sql = applySkip(command, sql);
            sql = applyTake(command, limit, sql);

            command.AppendQuery(sql);
        }

        public string AppendWhere(NpgsqlCommand command, string sql)
        {
            string filter = null;
            if (Where != null)
                filter = Where.ToSql(command);

            if (filter.IsNotEmpty())
                sql += " where " + filter;
            return sql;
        }

        private string applyTake(NpgsqlCommand command, int limit, string sql)
        {
            if (limit > 0)
            {
                sql += " LIMIT " + limit;
            }
            else
            {
                var take = Model.FindOperators<TakeResultOperator>().LastOrDefault();
                if (take != null)
                {
                    var param = command.AddParameter(take.Count.Value());
                    sql += " LIMIT :" + param.ParameterName;
                }
            }
            return sql;
        }

        private string applySkip(NpgsqlCommand command, string sql)
        {
            var skip = Model.FindOperators<SkipResultOperator>().LastOrDefault();
            if (skip != null)
            {
                var param = command.AddParameter(skip.Count.Value());
                sql += " OFFSET :" + param.ParameterName;
            }
            return sql;
        }

        private string determineOrderClause()
        {
            var orders = bodyClauses().OfType<OrderByClause>().SelectMany(x => x.Orderings).ToArray();
            if (!orders.Any()) return string.Empty;

            return " order by " + orders.Select(toOrderClause).Join(", ");
        }

        private string toOrderClause(Ordering clause)
        {
            var locator = _mapping.JsonLocator(clause.Expression);
            return clause.OrderingDirection == OrderingDirection.Asc
                ? locator
                : locator + " desc";
        }

        private IWhereFragment buildWhereFragment()
        {
            var bodies = bodyClauses();

            var wheres = bodies.OfType<WhereClause>().ToArray();
            if (wheres.Length == 0) return _mapping.DefaultWhereFragment();

            var where = wheres.Length == 1
                ? _schema.Parser.ParseWhereFragment(_mapping, wheres.Single().Predicate)
                : new CompoundWhereFragment(_schema.Parser, _mapping, "and", wheres);

            return _mapping.FilterDocuments(Model, where);
        }

        private IEnumerable<IBodyClause> bodyClauses()
        {
            var bodies = _subQuery == null
                ? Model.AllBodyClauses()
                : Model.BodyClauses.Take(_subQuery.Index);

            return bodies;
        }
    }
}