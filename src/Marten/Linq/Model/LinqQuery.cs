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
    }

    public class LinqQuery<T> : ILinqQuery
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

            Selector = BuildSelector(joins, stats);
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

        // Leave this code here, because it will need to use the SubQuery logic in its selection
        public ISelector<T> BuildSelector(IIncludeJoin[] joins, QueryStatistics stats)
        {
            var selector = buildSelector<T>(_schema, _mapping, Model);

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

        private static ISelector<T> buildSelector<T>(IDocumentSchema schema, IQueryableDocument mapping,
            QueryModel query)
        {
            var selectable = query.AllResultOperators().OfType<ISelectableOperator>().FirstOrDefault();
            if (selectable != null)
            {
                return selectable.BuildSelector<T>(schema, mapping);
            }


            if (query.HasSelectMany())
            {
                return new SelectManyQuery(mapping, query, 0).ToSelector<T>(schema.StoreOptions.Serializer());
            }

            if (query.SelectClause.Selector.Type == query.SourceType())
            {
                if (typeof(T) == typeof(string))
                {
                    return (ISelector<T>)new JsonSelector();
                }

                // I'm so ashamed of this hack, but "simplest thing that works"
                if (typeof(T) == typeof(IEvent))
                {
                    return mapping.As<EventQueryMapping>().Selector.As<ISelector<T>>();
                }

                if (typeof(T) != query.SourceType())
                {
                    // TODO -- going to have to come back to this one.
                    return null;
                }

                var resolver = schema.ResolverFor<T>();

                return new WholeDocumentSelector<T>(mapping, resolver);
            }


            var visitor = new SelectorParser(query);
            visitor.Visit(query.SelectClause.Selector);

            return visitor.ToSelector<T>(schema, mapping);
        }
    }
}