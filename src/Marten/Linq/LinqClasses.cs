using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using FubuCore;
using Marten.Schema;
using Npgsql;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Parsing.Structure;

namespace Marten.Linq
{
    public class MartenQueryable<T> : QueryableBase<T>
    {
        public MartenQueryable(IQueryParser queryParser, IQueryExecutor executor) : base(queryParser, executor)
        {
        }

        public MartenQueryable(IQueryProvider provider) : base(provider)
        {
        }

        public MartenQueryable(IQueryProvider provider, Expression expression) : base(provider, expression)
        {
        }


    }



    public class DocumentQuery<T>
    {
        private readonly string _tableName;
        private readonly IList<NpgsqlParameter> _parameters = new List<NpgsqlParameter>(); 

        public DocumentQuery(string tableName)
        {
            _tableName = tableName;

        }

        public NpgsqlCommand ToCommand()
        {
            throw new NotImplementedException();
        }

        // TODO -- different overload that takes ctor arguments for the sproc idea later?
        public void Where(string sql, params object[] parameters)
        {
            
        }
    }

    public class WhereFragment
    {
        private readonly string _sql;
        private readonly object[] _parameters;

        public WhereFragment(string sql, params object[] parameters)
        {
            _sql = sql;
            _parameters = parameters;
        }
    }

    public static class SqlBuilder
    {


        public static string GetWhereClause(QueryModel model)
        {
            // TODO -- what if there is more than one?
            var @where = model.BodyClauses.OfType<WhereClause>().FirstOrDefault();
            if (@where == null) return null;

            if (@where.Predicate is BinaryExpression)
            {
                return GetWhereClause(@where.Predicate.As<BinaryExpression>());
            }

            return null;
        }

        public static string GetWhereClause(BinaryExpression model)
        {
            if (model.Method.Name == "Equals")
            {
                return "something";
            }

            throw new NotSupportedException("Marten does not yet support {0} as a where clause".ToFormat());
        }
    }



}