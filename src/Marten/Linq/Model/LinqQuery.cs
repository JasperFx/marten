using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
    /*
     * TODO's
     * 1. OrderBy uses the string builder
     * 2. Join uses the string builder
     * 3. Use the pool for the StringBuilder's
     */

    public interface ILinqQuery
    {
        QueryModel Model { get; }
        Type SourceType { get; }
        void ConfigureCommand(NpgsqlCommand command);
        void ConfigureCommand(NpgsqlCommand command, int limit);
        void AppendWhere(NpgsqlCommand command, StringBuilder sql);
        void ConfigureCount(NpgsqlCommand command);
        void ConfigureAny(NpgsqlCommand command);
        void ConfigureAggregate(NpgsqlCommand command, string @operator);
    }

    public class LinqQuery<T> : ILinqQuery
    {
        private readonly IIncludeJoin[] _joins;
        private readonly IQueryableDocument _mapping;
        private readonly IDocumentSchema _schema;

        private readonly SelectManyQuery _subQuery;
        private ISelector<T> _innerSelector;

        public LinqQuery(IDocumentSchema schema, QueryModel model, IIncludeJoin[] joins, QueryStatistics stats)
        {
            Model = model;
            _schema = schema;
            _joins = joins;
            _mapping = schema.MappingFor(model.SourceType()).ToQueryableDocument();

            for (var i = 0; i < model.BodyClauses.Count; i++)
            {
                var clause = model.BodyClauses[i];
                if (clause is AdditionalFromClause)
                {
                    // TODO -- to be able to go recursive, have _subQuery start to read the BodyClauses
                    _subQuery = new SelectManyQuery(schema, _mapping, model, i + 1);


                    break;
                }
            }

            Selector = BuildSelector(joins, stats, _subQuery, joins);
            SourceType = Model.SourceType();

            Where = buildWhereFragment();
        }

        public ISelector<T> Selector { get; }

        public IWhereFragment Where { get; set; }

        public QueryModel Model { get; }

        public Type SourceType { get; }

        public void ConfigureCommand(NpgsqlCommand command)
        {
            ConfigureCommand(command, 0);
        }

        public void ConfigureCommand(NpgsqlCommand command, int limit)
        {
            var isComplexSubQuery = _subQuery != null && _subQuery.IsComplex(_joins);

            // TODO -- move this to pool of StringBuilder's
            var sql = new StringBuilder();
            if (isComplexSubQuery)
            {
                if (_subQuery.HasSelectTransform())
                {
                    sql.Append("select ");
                    sql.Append(_subQuery.RawChildElementField());
                    sql.Append(" from ");
                    sql.Append(_mapping.Table.QualifiedName);
                    sql.Append(" as d");

                }
                else
                {
                    _innerSelector.WriteSelectClause(sql, _mapping);
                }
            }
            else
            {
                Selector.WriteSelectClause(sql, _mapping);
            }

            AppendWhere(command, sql);

            writeOrderClause(sql);

            if (isComplexSubQuery)
            {
                _subQuery.ConfigureCommand(_joins, Selector, command, sql, limit);
            }
            else
            {
                Model.ApplySkip(command, sql);
                Model.ApplyTake(command, limit, sql);
            }

            command.AppendQuery(sql.ToString());
        }


        public void AppendWhere(NpgsqlCommand command, StringBuilder sql)
        {
            string filter = null;
            if (Where != null)
            {
                filter = Where.ToSql(command);
            }

            if (filter.IsNotEmpty())
            {
                sql.Append(" where ");
                sql.Append(filter);
            }
        }

        public void ConfigureCount(NpgsqlCommand command)
        {
            // TODO -- take it from a pool
            var sql = new StringBuilder();


            if (_subQuery != null)
            {
                if (Model.HasOperator<DistinctResultOperator>())
                    throw new NotSupportedException(
                        "Marten does not yet support SelectMany() with both a Distinct() and Count() operator");

                // TODO -- this will need to be smarter
                sql.Append("select sum(jsonb_array_length(");
                sql.Append(_subQuery.SqlLocator);
                sql.Append(")) as number");
            }
            else
            {
                sql.Append("select count(*) as number");
            }

            sql.Append(" from ");
            sql.Append(_mapping.Table.QualifiedName);
            sql.Append(" as d");

            AppendWhere(command, sql);

            command.AppendQuery(sql.ToString());
        }

        public void ConfigureAny(NpgsqlCommand command)
        {
            // TODO -- pull from an object pool
            var sql = new StringBuilder();

            if (_subQuery != null)
            {
                sql.Append("select (sum(jsonb_array_length(");
                sql.Append(_subQuery.SqlLocator);
                sql.Append(")) > 0) as result");
            }
            else
            {
                sql.Append("select (count(*) > 0) as result");
            }

            sql.Append(" from ");
            sql.Append(_mapping.Table.QualifiedName);
            sql.Append(" as d");


            new LinqQuery<bool>(_schema, Model, new IIncludeJoin[0], null).AppendWhere(command, sql);

            command.AppendQuery(sql.ToString());
        }

        public void ConfigureAggregate(NpgsqlCommand command, string @operator)
        {
            var locator = _mapping.JsonLocator(Model.SelectClause.Selector);
            var field = @operator.ToFormat(locator);

            // TODO -- pull from a pool
            var sql = new StringBuilder();

            sql.Append("select ");
            sql.Append(field);
            sql.Append(" from ");
            sql.Append(_mapping.Table.QualifiedName);
            sql.Append(" as d");

            AppendWhere(command, sql);

            command.AppendQuery(sql.ToString());
        }


        public IQueryHandler<IList<T>> ToList()
        {
            return new ListQueryHandler<T>(this);
        }

        private void writeOrderClause(StringBuilder sql)
        {
            var orders = bodyClauses().OfType<OrderByClause>().SelectMany(x => x.Orderings).ToArray();
            if (!orders.Any()) return;

            sql.Append(" order by ");
            writeOrderByFragment(sql, orders[0]);
            for (int i = 1; i < orders.Length; i++)
            {
                sql.Append(", ");
                writeOrderByFragment(sql, orders[i]);
            }
        }



        private void writeOrderByFragment(StringBuilder sql, Ordering clause)
        {
            var locator = _mapping.JsonLocator(clause.Expression);
            sql.Append(locator);

            if (clause.OrderingDirection == OrderingDirection.Desc)
            {
                sql.Append(" desc");
            }
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

        public IQueryHandler<TResult> ToCount<TResult>()
        {
            return new CountQueryHandler<TResult>(this);
        }

        public IQueryHandler<bool> ToAny()
        {
            return new AnyQueryHandler(this);
        }

        // Leave this code here, because it will need to use the SubQuery logic in its selection
        public ISelector<T> BuildSelector(IIncludeJoin[] joins, QueryStatistics stats, SelectManyQuery subQuery,
            IIncludeJoin[] includeJoins)
        {
            var selector =
                _innerSelector = SelectorParser.ChooseSelector<T>("d.data", _schema, _mapping, Model, subQuery, joins);

            if (stats != null)
            {
                selector = new StatsSelector<T>(selector);
            }

            if (joins.Any())
            {
                selector = new IncludeSelector<T>(_schema, selector, joins);
            }

            return selector;
        }
    }
}