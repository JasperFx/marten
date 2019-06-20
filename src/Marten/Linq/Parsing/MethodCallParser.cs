using System;
using System.Linq.Expressions;
using System.Reflection;
using Baseline.Reflection;
using Marten.Schema;

namespace Marten.Linq.Parsing
{
    public abstract class MethodCallParser<T>: IMethodCallParser
    {
        private readonly MethodInfo _method;

        public MethodCallParser(Expression<Action<T>> method)
        {
            _method = ReflectionHelper.GetMethod(method);
        }

        public bool Matches(MethodCallExpression expression)
        {
            // You cannot use the Equals() method on any Reflection objects, they
            // only check for reference equality. Ask me how I know that;)
            return expression.Object?.Type == typeof(T) && expression.Method.Name == _method.Name;
        }

        public abstract IWhereFragment Parse(IQueryableDocument mapping, ISerializer serializer, MethodCallExpression expression);
    }
}
