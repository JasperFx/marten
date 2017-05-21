using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Baseline;
using Baseline.Reflection;
using Marten.Linq.Parsing;
using Marten.Schema;
using Marten.Util;

namespace Marten.Linq.Compiled
{
    public class CompiledQueryMemberExpressionVisitor : ExpressionVisitor
    {
        private static readonly IList<StringComparisonParser> _stringMethods
            = new List<StringComparisonParser> {new StringContains(), new StringEndsWith(), new StringStartsWith()};

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

        private readonly IQueryableDocument _mapping;
        private readonly Type _queryType;
        private readonly ISerializer _serializer;
        private IField _lastMember;
        private static readonly string[] _skippedMethods = new[] {nameof(CompiledQueryExtensions.Include),nameof(CompiledQueryExtensions.Stats), };
        private StringComparisonParser _parser;

        public CompiledQueryMemberExpressionVisitor(IQueryableDocument mapping, Type queryType, ISerializer serializer)
        {
            _mapping = mapping;
            _queryType = queryType;
            _serializer = serializer;
        }

        internal IList<IDbParameterSetter> ParameterSetters { get; } = new List<IDbParameterSetter>();

        protected override Expression VisitUnary(UnaryExpression node)
        {
            if (node.Operand is LambdaExpression && node.Operand.As<LambdaExpression>().ReturnType == typeof(bool))
            {
                var body = node.Operand.As<LambdaExpression>().Body;
                if (node.NodeType == ExpressionType.Not)
                {
                    throw new NotImplementedException();
                }
                else if (body.NodeType == ExpressionType.MemberAccess)
                {
                    ParameterSetters.Add(new ConstantDbParameterSetter(true));
                }
            }

            return base.VisitUnary(node);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            _lastMember = _mapping.FieldFor(new MemberInfo[] { node.Member });

            if (node.NodeType == ExpressionType.MemberAccess && node.Member.DeclaringType == _queryType)
            {
                var property = (PropertyInfo)node.Member;

                var method = GetType().GetMethod(nameof(CreateParameterSetter), BindingFlags.Instance | BindingFlags.NonPublic).MakeGenericMethod(_queryType, property.PropertyType);
                var result = (IDbParameterSetter)method.Invoke(this, new[] { property });
                ParameterSetters.Add(result);
            }

            return base.VisitMember(node);
        }

        protected override Expression VisitConstant(ConstantExpression node)
        {
            if (node.Type != _queryType)
            {
                var value = _lastMember.GetValue(node);

                var setter = new ConstantDbParameterSetter(value);
                ParameterSetters.Add(setter);
            }

            return base.VisitConstant(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            // skip Visiting Include or Stats method members
            if (_skippedMethods.Contains(node.Method.Name)) return node;

            if (node.Method.HasAttribute<SkipOnCompiledQueryParsingAttribute>()) return node;

            if (IsContainmentMethod(node.Method))
            {
                var visitor = new ContainmentParameterVisitor(_serializer, _queryType, ParameterSetters);
                return visitor.Visit(node);
            }

            _parser = _stringMethods.FirstOrDefault(x => x.Matches(node));

            try
            {
                return base.VisitMethodCall(node);
            }
            finally
            {
                _parser = null;
            }
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

            return new DbParameterSetter<TObject, object>(getter)
            {
                Parser = _parser
            };
        }
    }
}