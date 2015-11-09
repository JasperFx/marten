using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Metadata;
using FubuCore;
using FubuCore.Reflection;
using Marten.Schema;
using Marten.Util;
using Remotion.Linq.Parsing;

namespace Marten.Linq
{
    

    public class MartenExpressionParser
    {
        private readonly DocumentQuery _query;
        private static readonly string CONTAINS = ReflectionHelper.GetMethod<string>(x => x.Contains("null")).Name;
        private static readonly string STARTS_WITH = ReflectionHelper.GetMethod<string>(x => x.StartsWith("null")).Name;
        private static readonly string ENDS_WITH = ReflectionHelper.GetMethod<string>(x => x.EndsWith("null")).Name;

        private static readonly IDictionary<ExpressionType, string> _operators = new Dictionary<ExpressionType, string>
        {
            {ExpressionType.Equal, "="},
            {ExpressionType.NotEqual, "!="},
            {ExpressionType.GreaterThan, ">"},
            {ExpressionType.GreaterThanOrEqual, ">="},
            {ExpressionType.LessThan, "<"},
            {ExpressionType.LessThanOrEqual, "<="}
        };

        public MartenExpressionParser(DocumentQuery query)
        {
            _query = query;
        }

        public IWhereFragment ParseWhereFragment(DocumentMapping mapping, Expression expression)
        {
            if (expression is BinaryExpression)
            {
                return GetWhereFragment(mapping, expression.As<BinaryExpression>());
            }

            if (expression.NodeType == ExpressionType.Call)
            {
                return GetMethodCall(mapping, expression.As<MethodCallExpression>());
            }

            if (expression is MemberExpression && expression.Type == typeof(bool))
            {
                var locator = JsonLocator(mapping, expression.As<MemberExpression>());
                return new WhereFragment("{0} = True".ToFormat(locator), true);
            }

            if (expression.NodeType == ExpressionType.Not)
            {
                return GetNotWhereFragment(mapping, expression.As<UnaryExpression>().Operand);
            }


            throw new NotSupportedException();
        }

        private IWhereFragment GetNotWhereFragment(DocumentMapping mapping, Expression expression)
        {
            if (expression is MemberExpression && expression.Type == typeof(bool))
            {
                var locator = JsonLocator(mapping, expression.As<MemberExpression>());
                return new WhereFragment("({0})::Boolean = False".ToFormat(locator));
            }

            if (expression.Type == typeof (bool) && expression.NodeType == ExpressionType.NotEqual && expression is BinaryExpression)
            {
                var binaryExpression = expression.As<BinaryExpression>();
                var locator = JsonLocator(mapping, binaryExpression.Left);
                if (binaryExpression.Right.NodeType == ExpressionType.Constant && binaryExpression.Right.As<ConstantExpression>().Value == null)
                {
                    return new WhereFragment($"({locator})::Boolean IS NULL");
                }
            }

            throw new NotSupportedException();
        }

        private IWhereFragment GetMethodCall(DocumentMapping mapping, MethodCallExpression expression)
        {

            // TODO -- generalize this mess
            if (expression.Method.Name == CONTAINS)
            {
                var @object = expression.Object;

                if (@object.Type == typeof (string))
                {
                    var locator = JsonLocator(mapping, @object);
                    var value = Value(expression.Arguments.Single()).As<string>();
                    return new WhereFragment("{0} like ?".ToFormat(locator), "%" + value + "%");
                }
            }

            if (expression.Method.Name == STARTS_WITH)
            {
                var @object = expression.Object;
                if (@object.Type == typeof(string))
                {
                    var locator = JsonLocator(mapping, @object);
                    var value = Value(expression.Arguments.Single()).As<string>();
                    return new WhereFragment("{0} like ?".ToFormat(locator), value + "%");
                }
            }

            if (expression.Method.Name == ENDS_WITH)
            {
                var @object = expression.Object;
                if (@object.Type == typeof(string))
                {
                    var locator = JsonLocator(mapping, @object);
                    var value = Value(expression.Arguments.Single()).As<string>();
                    return new WhereFragment("{0} like ?".ToFormat(locator), "%" + value);
                }
            }

            throw new NotImplementedException();
        }

        public IWhereFragment GetWhereFragment(DocumentMapping mapping, BinaryExpression binary)
        {
            if (_operators.ContainsKey(binary.NodeType))
            {
                return buildSimpleWhereClause(mapping, binary);
            }


            switch (binary.NodeType)
            {
                case ExpressionType.AndAlso:
                    return new CompoundWhereFragment("and", ParseWhereFragment(mapping, binary.Left),
                        ParseWhereFragment(mapping, binary.Right));

                case ExpressionType.OrElse:
                    return new CompoundWhereFragment("or", ParseWhereFragment(mapping, binary.Left),
                        ParseWhereFragment(mapping, binary.Right));
            }

            throw new NotSupportedException();
        }

        private IWhereFragment buildSimpleWhereClause(DocumentMapping mapping, BinaryExpression binary)
        {
            var jsonLocator = JsonLocator(mapping, binary.Left);
            var op = _operators[binary.NodeType];

            var value = Value(binary.Right);

            if (value == null)
            {
                var sql = binary.NodeType == ExpressionType.NotEqual
                    ? $"{jsonLocator} is not null"
                    : $"{jsonLocator} is null";

                return new WhereFragment(sql);
            }

            
            return new WhereFragment("{0} {1} ?".ToFormat(jsonLocator, op), value);
        }

        public static object Value(Expression expression)
        {
            if (expression is ConstantExpression)
            {
                // TODO -- handle nulls
                // TODO -- check out more types here.
                return expression.As<ConstantExpression>().Value;
            }

            throw new NotSupportedException();
        }

        // TODO -- use the mapping off of DocumentQuery later
        public string JsonLocator(DocumentMapping mapping, Expression expression)
        {
            var visitor = new FindMembers();
            visitor.Visit(expression);

            //return new JsonLocatorField(visitor.Members.ToArray()).SqlLocator;

            var field = mapping.FieldFor(visitor.Members);

            _query.RegisterField(field);

            return field.SqlLocator;
        }


    }

    public class FindMembers : RelinqExpressionVisitor    {
        public readonly IList<MemberInfo> Members = new List<MemberInfo>(); 

        protected override Expression VisitMember(MemberExpression node)
        {
            Members.Insert(0, node.Member);

            return base.VisitMember(node);
        }
    }
}