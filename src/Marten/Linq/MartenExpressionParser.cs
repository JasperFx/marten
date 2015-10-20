using System;
using System.Linq.Expressions;
using System.Reflection;
using FubuCore;
using Marten.Util;

namespace Marten.Linq
{
    public static class MartenExpressionParser
    {
        public static IWhereFragment ParseWhereFragment(Expression expression)
        {
            if (expression is BinaryExpression)
            {
                return GetWhereFragment(expression.As<BinaryExpression>());
            }

            throw new NotSupportedException();
        }

        public static IWhereFragment GetWhereFragment(BinaryExpression binary)
        {
            switch (binary.NodeType)
            {
                case ExpressionType.Equal:
                    // TODO -- handle NULL differently I'd imagine
                    var value = Value(binary.Right);
                    var sql = "{0} = ?".ToFormat(JsonLocator(binary.Left));
                    

                    return new WhereFragment(sql, value);
            }

            throw new NotSupportedException();
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
                if (memberType == typeof (int))
                {
                    return "CAST({0} as integer)".ToFormat(locator);
                }


                return locator;
            }

            throw new NotSupportedException();
        }
    }
}