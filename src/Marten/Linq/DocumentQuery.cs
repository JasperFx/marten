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
        private readonly IDocumentSchema _schema;
        private readonly QueryModel _query;

        public DocumentQuery(IDocumentSchema schema, QueryModel query)
        {
            var rootType = query.SourceType();
            var mapping = schema.MappingFor(rootType);
            _mapping = mapping;
            _schema = schema;
            _query = query;
        }



        public Type SourceDocumentType => _query.SourceType();

        [Obsolete("Will be superseeded by AnyQueryHandler<T>")]
        public void ConfigureForAny(NpgsqlCommand command)
        {
            var sql = "select (count(*) > 0) as result from " + _mapping.Table.QualifiedName + " as d";

            var where = BuildWhereClause();

            if (@where != null) sql += " where " + @where.ToSql(command);

            command.AppendQuery(sql);
        }

        public void ConfigureForCount(NpgsqlCommand command)
        {
            var sql = "select count(*) as number from " + _mapping.Table.QualifiedName + " as d";

            var where = BuildWhereClause();

            if (@where != null) sql += " where " + @where.ToSql(command);

            command.AppendQuery(sql);

        }

        private void ConfigureAggregateCommand(NpgsqlCommand command, string selectFormat)
        {
            var propToSum = _mapping.JsonLocator(_query.SelectClause.Selector);
            var sql = string.Format(selectFormat, propToSum, _mapping.Table.QualifiedName);

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



        public ISelector<T> ConfigureCommand<T>(IDocumentSchema schema, NpgsqlCommand command, int rowLimit = 0)
        {
            if (_query.HasOperator<LastResultOperator>())
            {
                throw new InvalidOperationException("Marten does not support the Last() or LastOrDefault() operations. Use a combination of ordering and First/FirstOrDefault() instead");
            }

            var selector = schema.ToSelectClause<T>(_mapping, _query);
            if (Includes.Any())
            {
                selector = new IncludeSelector<T>(schema, selector, Includes.ToArray());
            }

            var sql = selector.ToSelectClause(_mapping);

            var where = BuildWhereClause();
            var orderBy = _query.ToOrderClause(_mapping);

            if (@where != null) sql += " where " + @where.ToSql(command);

            if (orderBy.IsNotEmpty()) sql += orderBy;

            if (rowLimit > 0)
            {
                sql = sql + " LIMIT " + rowLimit;
            }
            else
            {
                sql = appendLimit(sql);
            }

            sql = _query.AppendOffset(sql);

            command.AppendQuery(sql);

            return selector;
        }

        public IList<IIncludeJoin> Includes { get; } = new List<IIncludeJoin>(); 

        private string appendLimit(string sql)
        {
            var take =
                _query.FindOperators<TakeResultOperator>().LastOrDefault();

            string limitNumber = null;
            if (take != null)
            {
                limitNumber = take.Count.ToString();
            }
            else if (_query.HasOperator<FirstResultOperator>())
            {
                limitNumber = "1";
            }
            // Got to return more than 1 to make it fail if there is more than one in the db
            else if (_query.HasOperator<SingleResultOperator>())
            {
                limitNumber = "2";
            }

            return limitNumber == null ? sql : sql + " LIMIT " + limitNumber + " ";
        }

        public IWhereFragment BuildWhereClause()
        {
            return _schema.BuildWhereFragment(_mapping, _query);
        }
    }

    
}