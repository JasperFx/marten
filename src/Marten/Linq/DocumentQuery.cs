using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Schema;
using Marten.Services.Includes;
using Marten.Util;
using Npgsql;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Clauses.ResultOperators;

namespace Marten.Linq
{
    public class DocumentQuery
    {
        private readonly IDocumentMapping _mapping;
        private readonly QueryModel _query;
        private readonly MartenExpressionParser _parser;

        public DocumentQuery(IDocumentMapping mapping, QueryModel query, MartenExpressionParser parser)
        {
            _mapping = mapping;
            _query = query;
            _parser = parser;
        }

        private IEnumerable<ResultOperatorBase> allResultOperators()
        {
            foreach (var @operator in _query.ResultOperators)
            {
                yield return @operator;
            }

            if (_query.MainFromClause.FromExpression is SubQueryExpression)
            {
                foreach (var @operator in _query.MainFromClause.FromExpression.As<SubQueryExpression>().QueryModel.ResultOperators)
                {
                    yield return @operator;
                }
            }
        }

        private T findOperator<T>() where T : ResultOperatorBase
        {
            return allResultOperators().OfType<T>().FirstOrDefault();
        }

        private IEnumerable<T> findOperators<T>() where T : ResultOperatorBase
        {
            return allResultOperators().OfType<T>();
        }

        private bool hasOperator<T>() where T : ResultOperatorBase
        {
            return allResultOperators().OfType<T>().Any();
        }

        public Type SourceDocumentType => _query.MainFromClause.ItemType;

        public void ConfigureForAny(NpgsqlCommand command)
        {
            var sql = "select (count(*) > 0) as result from " + _mapping.QualifiedTableName + " as d";

            var where = BuildWhereClause();

            if (@where != null) sql += " where " + @where.ToSql(command);

            command.AppendQuery(sql);
        }

        public void ConfigureForCount(NpgsqlCommand command)
        {
            var sql = "select count(*) as number from " + _mapping.QualifiedTableName + " as d";

            var where = BuildWhereClause();

            if (@where != null) sql += " where " + @where.ToSql(command);

            command.AppendQuery(sql);

        }

        private void ConfigureAggregateCommand(NpgsqlCommand command, string selectFormat)
        {
            var propToSum = _mapping.JsonLocator(_query.SelectClause.Selector);
            var sql = string.Format(selectFormat, propToSum, _mapping.QualifiedTableName);

            var where = BuildWhereClause();

            if (@where != null) sql += " where " + @where.ToSql(command);

            command.AppendQuery(sql);
        }

        public void ConfigureForSum(NpgsqlCommand command)
        {
            ConfigureAggregateCommand(command, "select sum({0}) as number from {1} as d");
        }

        public void ConfigureForMax(NpgsqlCommand command)
        {
            ConfigureAggregateCommand(command, "select max({0}) as number from {1} as d");
        }

        public void ConfigureForMin(NpgsqlCommand command)
        {
            ConfigureAggregateCommand(command, "select min({0}) as number from {1} as d");
        }

        public void ConfigureForAverage(NpgsqlCommand command)
        {
            ConfigureAggregateCommand(command, "select avg({0}) as number from {1} as d");
        }



        public ISelector<T> ConfigureCommand<T>(IDocumentSchema schema, NpgsqlCommand command)
        {
            if (hasOperator<LastResultOperator>())
            {
                throw new InvalidOperationException("Marten does not support the Last() or LastOrDefault() operations. Use a combination of ordering and First/FirstOrDefault() instead");
            }

            var documentStorage = schema.StorageFor(_mapping.DocumentType);
            return ConfigureCommand<T>(schema, documentStorage, command);
        }

        public IList<IIncludeJoin> Includes { get; } = new List<IIncludeJoin>(); 

        public ISelector<T> ConfigureCommand<T>(IDocumentSchema schema, IDocumentStorage documentStorage, NpgsqlCommand command)
        {
            var select = buildSelectClause<T>(schema, documentStorage);
            var sql = $"select {@select.SelectFields().Join(", ")} from {_mapping.QualifiedTableName} as d";

            if (Includes.Any())
            {
                sql = $"{sql} {Includes.Select(x => x.JoinText).Join(" ")}";
            }

            var where = BuildWhereClause();
            var orderBy = toOrderClause();

            if (@where != null) sql += " where " + @where.ToSql(command);

            if (orderBy.IsNotEmpty()) sql += orderBy;

            sql = appendLimit(sql);
            sql = appendOffset(sql);

            command.AppendQuery(sql);

            return @select;
        }

        private ISelector<T> buildSelectClause<T>(IDocumentSchema schema, IDocumentStorage storage)
        {
            ISelector<T> selector = null;

            if (_query.SelectClause.Selector.Type == _query.MainFromClause.ItemType)
            {
                selector = new WholeDocumentSelector<T>(_mapping, storage.As<IResolver<T>>());
            }
            else
            {
                var visitor = new SelectorParser();
                visitor.Visit(_query.SelectClause.Selector);

                selector = visitor.ToSelector<T>(_mapping);
            }

            Includes.Each(x => selector = x.WrapSelector(schema, selector));

            return selector;
        }

        private string appendOffset(string sql)
        {
            var take = findOperators<SkipResultOperator>().LastOrDefault();

            return take == null ? sql : sql + " OFFSET " + take.Count + " ";
        }

        private string appendLimit(string sql)
        {
            var take =
                findOperators<TakeResultOperator>().LastOrDefault();

            string limitNumber = null;
            if (take != null)
            {
                limitNumber = take.Count.ToString();
            }
            else if (hasOperator<FirstResultOperator>())
            {
                limitNumber = "1";
            }
            // Got to return more than 1 to make it fail if there is more than one in the db
            else if (hasOperator<SingleResultOperator>())
            {
                limitNumber = "2";
            }

            return limitNumber == null ? sql : sql + " LIMIT " + limitNumber + " ";
        }

        private string toOrderClause()
        {
            var orders = _query.BodyClauses.OfType<OrderByClause>().SelectMany(x => x.Orderings).ToArray();
            if (!orders.Any()) return string.Empty;

            return " order by " + orders.Select(ToOrderClause).Join(", ");
        }

        public string ToOrderClause(Ordering clause)
        {
            var locator = _mapping.JsonLocator(clause.Expression);
            return clause.OrderingDirection == OrderingDirection.Asc
                ? locator
                : locator + " desc";
        }

        public IWhereFragment BuildWhereClause()
        {
            var wheres = _query.BodyClauses.OfType<WhereClause>().ToArray();
            if (wheres.Length == 0) return _mapping.DefaultWhereFragment();

            var where = wheres.Length == 1
                ? _parser.ParseWhereFragment(_mapping, wheres.Single().Predicate)
                : new CompoundWhereFragment(_parser, _mapping, "and", wheres);

            return _mapping.FilterDocuments(where);
        }
    }

    
}