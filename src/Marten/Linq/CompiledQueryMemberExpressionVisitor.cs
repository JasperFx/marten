using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace Marten.Linq
{
    internal class CompiledQueryMemberExpressionVisitor : ExpressionVisitor
    {
        private IList<IDbParameterSetter> _parameterSetters = new List<IDbParameterSetter>();
        private readonly Type _queryType;

        public CompiledQueryMemberExpressionVisitor(Type queryType)
        {
            _queryType = queryType;
        }

        public IList<IDbParameterSetter> ParameterSetters => _parameterSetters;

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.NodeType == ExpressionType.MemberAccess && node.Member.ReflectedType == _queryType)
            {
                var property = (PropertyInfo)node.Member;
                var method = GetType().GetMethod("CreateParameterSetter", BindingFlags.Instance | BindingFlags.NonPublic).MakeGenericMethod(_queryType, property.PropertyType);
                var result = (IDbParameterSetter)method.Invoke(this, new []{property});
                _parameterSetters.Add(result);
            }
            return base.VisitMember(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            // skip Visiting Include method members
            return node.Method.Name.Contains("Include") ? node : base.VisitMethodCall(node);
        }

        public static Func<TTarget, TProperty> CompileGetter<TTarget, TProperty>(PropertyInfo property)
        {
            ParameterExpression target = Expression.Parameter(property.ReflectedType, "target");
            MethodInfo method = property.GetGetMethod();

            MethodCallExpression callGetMethod = Expression.Call(target, method);

            var lambda = Expression.Lambda<Func<TTarget, TProperty>>(callGetMethod, target);

            return lambda.Compile();
        }


        private IDbParameterSetter CreateParameterSetter<TObject, TProperty>(PropertyInfo property)
        {
            return new DbParameterSetter<TObject, TProperty>(CompileGetter<TObject, TProperty>(property));
        }
    }
}