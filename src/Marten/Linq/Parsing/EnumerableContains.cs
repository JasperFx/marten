using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Marten.Schema;

namespace Marten.Linq.Parsing
{
    public class EnumerableContains : IMethodCallParser
    {
        public bool Matches(MethodCallExpression expression)
        {
            return expression.Method.Name == MartenExpressionParser.CONTAINS &&
                   typeMatches(expression.Object.Type) &&
                   expression.Arguments.Single().IsValueExpression();
        }

        private static bool typeMatches(Type type)
        {
            if (type.IsGenericEnumerable()) return true;

            return type.Closes(typeof(IReadOnlyList<>));
        }

        public IWhereFragment Parse(IQueryableDocument mapping, ISerializer serializer, MethodCallExpression expression)
        {
            var value = expression.Arguments.Single().Value();
            return ContainmentWhereFragment.SimpleArrayContains(serializer, expression.Object, value);
        }
    }
}