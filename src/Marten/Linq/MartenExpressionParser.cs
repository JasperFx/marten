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
        private static readonly Dictionary<Type, string> _pgCasts = new Dictionary<Type, string>
        {
            {typeof (int), "integer"},
            {typeof (long), "bigint"}
        };

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
            if (!_pgCasts.ContainsKey(memberType))
                throw new ArgumentOutOfRangeException("memberType",
                    "There is not Postgresql cast for member type " + memberType.FullName);

            return "CAST({0} as {1})".ToFormat(locator, _pgCasts[memberType]);
        }

        public static IWhereFragment ParseWhereFragment(Expression expression)
        {
            if (expression is BinaryExpression)
            {
                return GetWhereFragment(expression.As<BinaryExpression>());
            }

            if (expression.NodeType == ExpressionType.Call)
            {
                return GetMethodCall(expression.As<MethodCallExpression>());
            }

            throw new NotSupportedException();
        }

        private static IWhereFragment GetMethodCall(MethodCallExpression expression)
        {
            if (expression.Method.Name == "Contains")
            {
                var @object = expression.Object;
                if (@object.Type == typeof (string))
                {
                    var locator = JsonLocator(@object);
                    var value = Value(expression.Arguments.Single()).As<string>();
                    return new WhereFragment("{0} like ?".ToFormat(locator), "%" + value + "%");
                }
            }

            throw new NotImplementedException();
        }

        public static IWhereFragment GetWhereFragment(BinaryExpression binary)
        {
            if (_operators.ContainsKey(binary.NodeType))
            {
                return buildSimpleWhereClause(binary);
            }


            switch (binary.NodeType)
            {
                case ExpressionType.AndAlso:
                    return new CompoundWhereFragment("and", ParseWhereFragment(binary.Left),
                        ParseWhereFragment(binary.Right));

                case ExpressionType.OrElse:
                    return new CompoundWhereFragment("or", ParseWhereFragment(binary.Left),
                        ParseWhereFragment(binary.Right));
            }

            throw new NotSupportedException();
        }

        private static IWhereFragment buildSimpleWhereClause(BinaryExpression binary)
        {
            var jsonLocator = JsonLocator(binary.Left);
            // TODO -- handle NULL differently I'd imagine
            var value = Value(binary.Right);

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

        public static string JsonLocator(Expression expression)
        {
            if (expression is MemberExpression)
            {
                var member = expression.As<MemberExpression>().Member;

                var locator = "data ->> '{0}'".ToFormat(member.Name);
                var memberType = member.GetMemberType();

                return memberType == typeof (string) ? locator : locator.ApplyCastToLocator(memberType);
            }

            throw new NotSupportedException();
        }
    }
}