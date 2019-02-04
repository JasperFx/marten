using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Linq.QueryHandlers;
using Marten.Schema;
using Marten.Services.Includes;
using Marten.Util;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.ResultOperators;

namespace Marten.Linq.Model
{
    public interface ILinqQuery
    {
        QueryModel Model { get; }
        Type SourceType { get; }

        void ConfigureCommand(CommandBuilder command);

        void ConfigureCommand(CommandBuilder command, int limit);

        void AppendWhere(CommandBuilder sql1);

        void ConfigureCount(CommandBuilder command);

        void ConfigureAny(CommandBuilder command);

        void ConfigureAggregate(CommandBuilder command, string @operator);
    }

    public class LinqQuery<T> : ILinqQuery
    {
        private readonly DocumentStore _store;
        private readonly IIncludeJoin[] _joins;
        private readonly IQueryableDocument _mapping;

        private readonly SelectManyQuery _subQuery;
        private ISelector<T> _innerSelector;

        public LinqQuery(DocumentStore store, QueryModel model, IIncludeJoin[] joins, QueryStatistics stats)
        {
            Model = model;
            _store = store;
            _joins = joins;

            // TODO -- going to have to push in the ITenant eventually
            _mapping = store.Tenancy.Default.MappingFor(model.SourceType()).ToQueryableDocument();

            for (var i = 0; i < model.BodyClauses.Count; i++)
            {
                var clause = model.BodyClauses[i];
                if (clause is AdditionalFromClause)
                {
                    // TODO -- to be able to go recursive, have _subQuery start to read the BodyClauses
                    _subQuery = new SelectManyQuery(store, _mapping, model, i + 1);

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

        public void ConfigureCommand(CommandBuilder command)
        {
            ConfigureCommand(command, 0);
        }

        public void ConfigureCommand(CommandBuilder sql, int limit)
        {
            var isComplexSubQuery = _subQuery != null && _subQuery.IsComplex(_joins);

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

            AppendWhere(sql);

            writeOrderClause(sql);

            if (isComplexSubQuery)
            {
                _subQuery.ConfigureCommand(_joins, Selector, sql, limit);
            }
            else
            {
                Model.ApplySkip(sql);
                Model.ApplyTake(limit, sql);
            }
        }

        public void AppendWhere(CommandBuilder sql)
        {
            if (Where != null)
            {
                sql.Append(" where ");
                Where.Apply(sql);
            }
        }

        public void ConfigureCount(CommandBuilder sql)
        {
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

            AppendWhere(sql);
        }

        public void ConfigureAny(CommandBuilder sql)
        {
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

            new LinqQuery<bool>(_store, Model, new IIncludeJoin[0], null).AppendWhere(sql);
        }

        public void ConfigureAggregate(CommandBuilder sql, string @operator)
        {
            var locator = _mapping.JsonLocator(Model.SelectClause.Selector);
            var field = @operator.ToFormat(locator);

            sql.Append("select ");
            sql.Append(field);
            sql.Append(" from ");
            sql.Append(_mapping.Table.QualifiedName);
            sql.Append(" as d");

            AppendWhere(sql);
        }

        public IQueryHandler<IReadOnlyList<T>> ToList()
        {
            return new ListQueryHandler<T>(this);
        }

        private void writeOrderClause(CommandBuilder sql)
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

        private void writeOrderByFragment(CommandBuilder sql, Ordering clause)
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
            if (wheres.Length == 0)
                return _mapping.DefaultWhereFragment();

            var where = wheres.Length == 1
                ? _store.Parser.ParseWhereFragment(_mapping, wheres.Single().Predicate)
                : new CompoundWhereFragment(_store.Parser, _mapping, "and", wheres);

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
                _innerSelector = SelectorParser.ChooseSelector<T>("d.data", _store.Tenancy.Default, _mapping, Model, subQuery, _store.Serializer, joins);

            if (stats != null)
            {
                selector = new StatsSelector<T>(selector);
            }

            if (joins.Any())
            {
                selector = new IncludeSelector<T>(_store.Storage, selector, joins);
            }

            return selector;
        }
    }
}