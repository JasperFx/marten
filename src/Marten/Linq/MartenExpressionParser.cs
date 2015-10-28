using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using FubuCore;
using Marten.Util;

namespace Marten.Linq
{
    

    public static class MartenExpressionParser
    {


        private static readonly IDictionary<ExpressionType, string> _operators = new Dictionary<ExpressionType, string>
        {
            {ExpressionType.Equal, "="},
            {ExpressionType.NotEqual, "!="},
            {ExpressionType.GreaterThan, ">"},
            {ExpressionType.GreaterThanOrEqual, ">="},
            {ExpressionType.LessThan, "<"},
            {ExpressionType.LessThanOrEqual, "<="}
        };

        public static string ApplyCastToLocator(this string locator, Type memberType)
        {
            if (memberType.IsEnum)
            {
                return "({0})::int".ToFormat(locator);
            }

            if (!TypeMappings.PgTypes.ContainsKey(memberType))
                throw new ArgumentOutOfRangeException("memberType",
                    "There is not Postgresql cast for member type " + memberType.FullName);

            return "CAST({0} as {1})".ToFormat(locator, TypeMappings.PgTypes[memberType]);
        }

        public static IWhereFragment ParseWhereFragment(Type rootType, Expression expression)
        {
            if (expression is BinaryExpression)
            {
                return GetWhereFragment(rootType, expression.As<BinaryExpression>());
            }

            if (expression.NodeType == ExpressionType.Call)
            {
                return GetMethodCall(rootType, expression.As<MethodCallExpression>());
            }

            throw new NotSupportedException();
        }

        private static IWhereFragment GetMethodCall(Type rootType, MethodCallExpression expression)
        {
            if (expression.Method.Name == "Contains")
            {
                var @object = expression.Object;
                if (@object.Type == typeof (string))
                {
                    var locator = JsonLocator(rootType, @object);
                    var value = Value(expression.Arguments.Single()).As<string>();
                    return new WhereFragment("{0} like ?".ToFormat(locator), "%" + value + "%");
                }
            }

            throw new NotImplementedException();
        }

        public static IWhereFragment GetWhereFragment(Type rootType, BinaryExpression binary)
        {
            if (_operators.ContainsKey(binary.NodeType))
            {
                return buildSimpleWhereClause(rootType, binary);
            }


            switch (binary.NodeType)
            {
                case ExpressionType.AndAlso:
                    return new CompoundWhereFragment("and", ParseWhereFragment(rootType, binary.Left),
                        ParseWhereFragment(rootType, binary.Right));

                case ExpressionType.OrElse:
                    return new CompoundWhereFragment("or", ParseWhereFragment(rootType, binary.Left),
                        ParseWhereFragment(rootType, binary.Right));
            }

            throw new NotSupportedException();
        }

        private static IWhereFragment buildSimpleWhereClause(Type rootType, BinaryExpression binary)
        {
            var jsonLocator = JsonLocator(rootType, binary.Left);
            // TODO -- handle NULL differently I'd imagine

            var value = Value(binary.Right);

            // Correct to the string value for enumerations
            if (value != null && value.GetType().IsEnum)
            {
                value = value.ToString();
            }

            var op = _operators[binary.NodeType];
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

        public static string JsonLocator(Type rootType, Expression expression)
        {
            if (expression is MemberExpression)
            {
                var memberExpression = expression.As<MemberExpression>();
                return JsonLocator(rootType, memberExpression);
            }

            if (expression is UnaryExpression)
            {
                return JsonLocator(rootType, expression.As<UnaryExpression>());
            }

            throw new NotSupportedException();
        }

        public static string JsonLocator(Type rootType, UnaryExpression expression)
        {
            if (expression.NodeType == ExpressionType.Convert)
            {
                return JsonLocator(rootType, expression.Operand);
            }

            throw new NotSupportedException();
        }

        public static string JsonLocator(Type rootType, MemberExpression memberExpression)
        {
            var memberType = memberExpression.Member.GetMemberType();

            var path = " ->> '{0}' ".ToFormat(memberExpression.Member.Name);
            var parent = memberExpression.Expression as MemberExpression;
            while (parent != null)
            {
                path = " -> '{0}' ".ToFormat(parent.Member.Name) + path;
                parent = parent.Expression as MemberExpression;
            }


            var locator = "data{0}".ToFormat(path).TrimEnd();


            if (memberType == typeof (string)) return locator;

            return locator.ApplyCastToLocator(memberType);
        }
    }
}