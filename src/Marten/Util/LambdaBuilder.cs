using System;
using System.Linq.Expressions;
using System.Reflection;

namespace Marten.Util
{
    public static class LambdaBuilder
    {
        public static Func<TTarget, TProperty> GetProperty<TTarget, TProperty>(PropertyInfo property)
        {
            var target = Expression.Parameter(property.ReflectedType, "target");
            var method = property.GetGetMethod();

            var callGetMethod = Expression.Call(target, method);

            var lambda = Expression.Lambda<Func<TTarget, TProperty>>(callGetMethod, target);

            return lambda.Compile();
        }


    }
}