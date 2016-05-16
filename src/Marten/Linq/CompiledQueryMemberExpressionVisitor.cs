using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Util;

namespace Marten.Linq
{
    internal class CompiledQueryMemberExpressionVisitor : ExpressionVisitor
    {
        private readonly IList<IDbParameterSetter> _parameterSetters = new List<IDbParameterSetter>();
        private readonly Type _queryType;

        public CompiledQueryMemberExpressionVisitor(Type queryType)
        {
            _queryType = queryType;
        }

        public IList<IDbParameterSetter> ParameterSetters => _parameterSetters;

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.NodeType == ExpressionType.MemberAccess)
            {
                if (node.Member.ReflectedType == _queryType)
                {
                    var property = (PropertyInfo) node.Member;
                    var method = GetType().GetMethod("CreateParameterSetter", BindingFlags.Instance | BindingFlags.NonPublic)
                            .MakeGenericMethod(_queryType, property.PropertyType);
                    var result = (IDbParameterSetter) method.Invoke(this, new[] {property});
                    _parameterSetters.Add(result);
                }
              
            }
            return base.VisitMember(node);
        }

        protected override Expression VisitConstant(ConstantExpression node)
        {
            if (node.Type != _queryType)
            {
                var result = new ConstantDbParameterSetter(node.Value);
                _parameterSetters.Add(result);
            }
            return base.VisitConstant(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            // skip Visiting Include method members
            return node.Method.Name.Contains("Include") ? node : base.VisitMethodCall(node);
        }


        private IDbParameterSetter CreateConstantParameterSetter<TObject, TProperty>(PropertyInfo property)
        {
            return new DbParameterSetter<TObject, TProperty>(LambdaBuilder.GetProperty<TObject, TProperty>(property));
        }

        private IDbParameterSetter CreateParameterSetter<TObject, TProperty>(PropertyInfo property)
        {
            return new DbParameterSetter<TObject, TProperty>(LambdaBuilder.GetProperty<TObject, TProperty>(property));
        }
    }
}