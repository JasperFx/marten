using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Marten.Linq.Fields;
using Marten.Linq.Filters;
using Marten.Linq.QueryHandlers;
using Marten.Linq.SqlGeneration;
using Marten.Schema;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing
{
    public class EnumerableContains: IMethodCallParser
    {
        public bool Matches(MethodCallExpression expression)
        {
            return expression.Method.Name == LinqConstants.CONTAINS &&
                   typeMatches(expression.Object.Type) &&
                   expression.Arguments.Single().IsValueExpression();
        }

        private static bool typeMatches(Type type)
        {
            if (type.IsGenericEnumerable())
                return true;

            return type.Closes(typeof(IReadOnlyList<>));
        }

        public ISqlFragment Parse(IFieldMapping mapping, ISerializer serializer, MethodCallExpression expression)
        {
            var value = expression.Arguments.Single().Value();
            return ContainmentWhereFragment.SimpleArrayContains(FindMembers.Determine(expression.Object), serializer, expression.Object, value);
        }
    }
}
