using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Events;
using Marten.Linq.QueryHandlers;
using Marten.Schema;
using Marten.Services.Includes;
using Marten.Transforms;
using Marten.Util;
using Npgsql;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.ResultOperators;

namespace Marten.Linq.Model
{
    public interface ILinqQuery
    {
        QueryModel Model { get; }
        Type SourceType { get; }
        void ConfigureCommand(NpgsqlCommand command);
        void ConfigureCommand(NpgsqlCommand command, int limit);
        string AppendWhere(NpgsqlCommand command, string sql);
        void ConfigureCount(NpgsqlCommand command);
        void ConfigureAny(NpgsqlCommand command);
        void ConfigureAggregate(NpgsqlCommand command, string @operator);
    }

    public class LinqQuery<T> : ILinqQuery
    {
        private readonly IQueryableDocument _mapping;
        private readonly IDocumentSchema _schema;
        private readonly IIncludeJoin[] _joins;

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
            var isComplexSubQuery = _subQuery != null && _subQuery.IsComplex(_joins);
            string sql = "";
            if (isComplexSubQuery)
            {
                if (_subQuery.HasSelectTransform())
                {
                    sql = $"select {_subQuery.RawChildElementField()} from {_mapping.Table.QualifiedName} as d";
                }
                else
                {
                    sql = _innerSelector.ToSelectClause(_mapping);
                }
            }
            else
            {
                sql = Selector.ToSelectClause(_mapping);
            }

            sql = AppendWhere(command, sql);

            var orderBy = determineOrderClause();

            if (orderBy.IsNotEmpty()) sql += orderBy;

            if (isComplexSubQuery)
            {
                sql = _subQuery.ConfigureCommand(_joins, Selector, command, sql, limit);
            }
            else
            {
                sql = Model.ApplySkip(command, sql);
                sql = Model.ApplyTake(command, limit, sql);
            }

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

        public void ConfigureCount(NpgsqlCommand command)
        {
            var select = "select count(*) as number";

            if (_subQuery != null)
            {
                if (Model.HasOperator<DistinctResultOperator>())
                {
                    throw new NotSupportedException("Marten does not yet support SelectMany() with both a Distinct() and Count() operator");
                }

                // TODO -- this will need to be smarter
                select = $"select sum(jsonb_array_length({_subQuery.SqlLocator})) as number";
            }

            var sql = $"{select} from {_mapping.Table.QualifiedName} as d";

            sql = AppendWhere(command, sql);

            command.AppendQuery(sql);
        }

        public IQueryHandler<TResult> ToCount<TResult>()
        {
            return new CountQueryHandler<TResult>(this);
        }

        public void ConfigureAny(NpgsqlCommand command)
        {
            var select = "select (count(*) > 0) as result";

            if (_subQuery != null)
            {
                select = $"select (sum(jsonb_array_length({_subQuery.SqlLocator})) > 0) as result";
            }

            var sql = $"{select} from {_mapping.Table.QualifiedName} as d";

            sql = new LinqQuery<bool>(_schema, Model, new IIncludeJoin[0], null).AppendWhere(command, sql);

            command.AppendQuery(sql);
        }

        public IQueryHandler<bool> ToAny()
        {
            return new AnyQueryHandler(this);
        }

        public void ConfigureAggregate(NpgsqlCommand command, string @operator)
        {
            var locator = _mapping.JsonLocator(Model.SelectClause.Selector);
            var field = @operator.ToFormat(locator);

            var sql = $"select {field} from {_mapping.Table.QualifiedName} as d";

            sql = AppendWhere(command, sql);

            command.AppendQuery(sql);
        }

        // Leave this code here, because it will need to use the SubQuery logic in its selection
        public ISelector<T> BuildSelector(IIncludeJoin[] joins, QueryStatistics stats, SelectManyQuery subQuery, IIncludeJoin[] includeJoins)
        {
            var selector = _innerSelector = SelectorParser.ChooseSelector<T>(_schema, _mapping, Model, subQuery, joins);

            if (stats != null)
            {
                selector = new StatsSelector<T>(stats, selector);
            }

            if (joins.Any())
            {
                selector = new IncludeSelector<T>(_schema, selector, joins);
            }

            return selector;
        }
    }
}