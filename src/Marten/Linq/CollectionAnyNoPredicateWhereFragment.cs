using System.Linq;
using Baseline;
using Npgsql;
using Remotion.Linq.Clauses.Expressions;

namespace Marten.Linq
{
    /// <summary>
    /// Handle Any() with JSONB_ARRAY_LENGTH introduced in PostgreSQL 9.4
    /// </summary>
    public class CollectionAnyNoPredicateWhereFragment : IWhereFragment
    {
        private readonly SubQueryExpression _expression;

        public CollectionAnyNoPredicateWhereFragment(SubQueryExpression expression)
        {            
            _expression = expression;
        }

        public string ToSql(NpgsqlCommand command)
        {            
            var visitor = new FindMembers();
            visitor.Visit(_expression.QueryModel.MainFromClause.FromExpression);
            var path = visitor.Members.Select(m => m.Name).Join("'->'");

            var query = $"JSONB_ARRAY_LENGTH(COALESCE(case when data->>'{path}' is not null then data->'{path}' else '[]' end)) > 0";
            return query;
        }
     
        public bool Contains(string sqlText)
        {
            return false;
        }
    }
}