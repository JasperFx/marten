using System.Linq;
using System.Reflection;
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
        private readonly MemberInfo[] _members;
        private readonly SubQueryExpression _expression;

        public CollectionAnyNoPredicateWhereFragment(MemberInfo[] members, SubQueryExpression expression)
        {
            _members = members;
            _expression = expression;
        }

        public string ToSql(NpgsqlCommand command)
        {            
            

            var path = _members.Select(m => m.Name).Join("'->'");

            if (_members.Length == 1)
            {
                return $"JSONB_ARRAY_LENGTH(COALESCE(case when data->>'{path}' is not null then data->'{path}' else '[]' end)) > 0";
            }
            else
            {
                return $"JSONB_ARRAY_LENGTH(COALESCE(case when data->'{path}' is not null then data->'{path}' else '[]' end)) > 0";
            }
        }
     
        public bool Contains(string sqlText)
        {
            return false;
        }
    }
}