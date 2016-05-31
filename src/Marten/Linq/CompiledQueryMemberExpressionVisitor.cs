using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Schema;
using Marten.Transforms;
using Marten.Util;

namespace Marten.Linq
{
    internal class CompiledQueryMemberExpressionVisitor : ExpressionVisitor
    {
        private readonly IList<IDbParameterSetter> _parameterSetters = new List<IDbParameterSetter>();
        private readonly IQueryableDocument _mapping;
        private readonly Type _queryType;
        private readonly EnumStorage _enumStorage;
        private IField _lastMember;

        public CompiledQueryMemberExpressionVisitor(IQueryableDocument mapping, Type queryType, EnumStorage enumStorage)
        {
            _mapping = mapping;
            _queryType = queryType;
            _enumStorage = enumStorage;
        }

        public IList<IDbParameterSetter> ParameterSetters => _parameterSetters;

        protected override Expression VisitMember(MemberExpression node)
        {
            _lastMember = _mapping.FieldFor(new MemberInfo[] {node.Member});

            if (node.NodeType == ExpressionType.MemberAccess && node.Member.ReflectedType == _queryType)
            {
                var property = (PropertyInfo)node.Member;
                var method = GetType().GetMethod(nameof(CreateParameterSetter), BindingFlags.Instance | BindingFlags.NonPublic).MakeGenericMethod(_queryType, property.PropertyType);
                var result = (IDbParameterSetter)method.Invoke(this, new []{property});
                _parameterSetters.Add(result);              
            }
            return base.VisitMember(node);
        }

        protected override Expression VisitConstant(ConstantExpression node)
        {
            if (node.Type != _queryType)
            {
                var value = _lastMember.GetValue(node);

                var setter = new ConstantDbParameterSetter(value);
                _parameterSetters.Add(setter);
            }

            return base.VisitConstant(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            // skip Visiting Include or Stats method members
            var skippedMethods = new[] {nameof(CompiledQueryExtensions.Include),nameof(CompiledQueryExtensions.Stats)};
            return skippedMethods.Contains(node.Method.Name) ? node : base.VisitMethodCall(node);
        }

        private IDbParameterSetter CreateParameterSetter<TObject, TProperty>(PropertyInfo property)
        {
            var getter = LambdaBuilder.GetProperty<TObject, object>(property);
            if (property.PropertyType.IsEnum && _enumStorage == EnumStorage.AsString)
            {
                getter = o =>
                {
                    var number = getter(o);
                    return Enum.GetName(property.PropertyType, number);
                };
            }

            return new DbParameterSetter<TObject, object>(getter);
        }
    }
}