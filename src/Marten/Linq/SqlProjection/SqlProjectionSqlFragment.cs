using System;
using System.Linq.Expressions;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlProjection
{
    public static class SqlProjectionSqlFragment
    {
        public static ISqlFragment TryParse(Expression expression, Func<Expression, Expression> visit = null)
        {
            if (expression == null)
            {
                return null;
            }

            if (expression is UnaryExpression { NodeType: ExpressionType.Convert } unaryExpression)
            {
                expression = unaryExpression.Operand;
            }

            visit ??= x => x;

            if (expression is not MethodCallExpression methodCall)
            {
                return null;
            }

            if (!methodCall.Method.IsGenericMethod ||
                methodCall.Method.GetGenericMethodDefinition() != SqlProjectionExtensions.MethodInfo)
            {
                return null;
            }

            if (visit(methodCall.Arguments[1]) is not ConstantExpression { Value: string sql })
            {
                throw new NotSupportedException("SqlProjection first parameter needs to resolve to a string");
            }

            if (visit(methodCall.Arguments[2]) is not ConstantExpression { Value: object[] sqlArguments })
            {
                throw new NotSupportedException("SqlProjection second parameter needs to resolve to an object[]");
            }

            var whereFragment = new WhereFragment(sql, sqlArguments);
            return whereFragment;
        }
    }
}
