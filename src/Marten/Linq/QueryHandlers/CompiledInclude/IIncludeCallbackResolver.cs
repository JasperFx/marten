using System;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Util;

namespace Marten.Linq.QueryHandlers.CompiledInclude
{
    public interface IIncludeCallbackResolver<TInclude>
    {
        Action<TInclude> Resolve(PropertyInfo property, IncludeTypeContainer typeContainer);
    }

    public abstract class IncludeCallbackResolver
    {

        protected static PropertyInfo GetPropertyInfo(PropertyInfo property, IncludeResultOperator @operator)
        {
            var target = Expression.Parameter(property.DeclaringType, "target");
            var method = property.GetGetMethod();

            var callGetMethod = Expression.Call(target, method);

            var lambda = Expression.Lambda<Func<IncludeResultOperator, LambdaExpression>>(callGetMethod, target);

            var compiledLambda = ExpressionCompiler.Compile<Func<IncludeResultOperator, LambdaExpression>>(lambda);
            var callback = compiledLambda.Invoke(@operator);
            var mi = (PropertyInfo)((MemberExpression)callback.Body).Member;
            return mi;
        }
    }
}