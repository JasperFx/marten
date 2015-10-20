using System;
using System.Linq.Expressions;
using FubuCore;

namespace Marten.Linq
{
    public static class SqlBuilder
    {
        public static IWhereFragment ParseWhereFragment(Expression expression)
        {
            if (expression is BinaryExpression)
            {
                return GetWhereFragment(expression.As<BinaryExpression>());
            }

            throw new NotImplementedException();
        }

        public static IWhereFragment GetWhereFragment(BinaryExpression binary)
        {
            switch (binary.NodeType)
            {
                case ExpressionType.Equal:
                    var sql = "{0} = ?".ToFormat(JsonLocator(binary.Left));
                    return new WhereFragment(sql, Value(binary.Right));
            }

            throw new NotImplementedException();
        }

        public static object Value(Expression expression)
        {
            if (expression is ConstantExpression)
            {
                return expression.As<ConstantExpression>().Value;
            }

            throw new NotImplementedException();
        }

        public static string JsonLocator(Expression expression)
        {
            if (expression is MemberExpression)
            {
                return "data ->> '{0}'".ToFormat(expression.As<MemberExpression>().Member.Name);
            }

            throw new NotImplementedException();
        }
    }
}