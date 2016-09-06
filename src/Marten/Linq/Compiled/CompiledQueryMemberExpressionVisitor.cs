using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Schema;
using Marten.Util;

namespace Marten.Linq.Compiled
{
    public class CompiledQueryMemberExpressionVisitor : ExpressionVisitor
    {
        public static bool IsContainmentMethod(MethodInfo method)
        {
            if (method.Name == nameof(Enumerable.Any)) return true;

            if (method.Name == nameof(Enumerable.Contains))
            {
                if (method.DeclaringType == typeof(string)) return false;

                return true;
            }

            return false;
        }

        private readonly IList<IDbParameterSetter> _parameterSetters = new List<IDbParameterSetter>();
        private readonly IQueryableDocument _mapping;
        private readonly Type _queryType;
        private readonly ISerializer _serializer;
        private IField _lastMember;
        private static readonly string[] _skippedMethods = new[] {nameof(CompiledQueryExtensions.Include),nameof(CompiledQueryExtensions.Stats)};

        public CompiledQueryMemberExpressionVisitor(IQueryableDocument mapping, Type queryType, ISerializer serializer)
        {
            _mapping = mapping;
            _queryType = queryType;
            _serializer = serializer;
        }

        public IList<IDbParameterSetter> ParameterSetters => _parameterSetters;

        protected override Expression VisitMember(MemberExpression node)
        {
            _lastMember = _mapping.FieldFor(new MemberInfo[] { node.Member });

            if (node.NodeType == ExpressionType.MemberAccess && node.Member.DeclaringType == _queryType)
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
            if (_skippedMethods.Contains(node.Method.Name)) return node;

            if (IsContainmentMethod(node.Method))
            {
                var visitor = new ContainmentParameterVisitor(_serializer, _queryType, _parameterSetters);
                return visitor.Visit(node);
            }

            return base.VisitMethodCall(node);
        }

        private IDbParameterSetter CreateParameterSetter<TObject, TProperty>(PropertyInfo property)
        {
            var getter = LambdaBuilder.GetProperty<TObject, object>(property);
            if (property.PropertyType.GetTypeInfo().IsEnum && _serializer.EnumStorage == EnumStorage.AsString)
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