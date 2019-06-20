using System.Reflection;
using Marten.Util;
using Remotion.Linq.Clauses.Expressions;

namespace Marten.Linq
{
    /// <summary>
    /// Handle Any() with JSONB_ARRAY_LENGTH introduced in PostgreSQL 9.4
    /// </summary>
    public class CollectionAnyNoPredicateWhereFragment: IWhereFragment
    {
        private readonly MemberInfo[] _members;
        private readonly SubQueryExpression _expression;

        public CollectionAnyNoPredicateWhereFragment(MemberInfo[] members, SubQueryExpression expression)
        {
            _members = members;
            _expression = expression;
        }

        public void Apply(CommandBuilder builder)
        {
            builder.Append("JSONB_ARRAY_LENGTH(COALESCE(case when ");
            builder.AppendPathToValue(_members, "data");

            builder.Append(" is not null then ");
            builder.AppendPathToObject(_members, "data");

            builder.Append(" else '[]' end)) > 0");
        }

        public bool Contains(string sqlText)
        {
            return false;
        }
    }
}
