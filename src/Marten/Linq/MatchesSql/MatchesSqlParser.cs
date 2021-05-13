using System.Linq.Expressions;
using System.Reflection;
using Baseline;
using Marten.Linq.Fields;
using Marten.Linq.Filters;
using Marten.Linq.Parsing;
using Marten.Linq.SqlGeneration;
using Marten.Schema;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.MatchesSql
{
    public class MatchesSqlParser: IMethodCallParser
    {
        private static readonly MethodInfo _sqlMethod =
            typeof(MatchesSqlExtensions).GetMethod(nameof(MatchesSqlExtensions.MatchesSql), new[] { typeof(object), typeof(string), typeof(object[]) });

        private static readonly MethodInfo _fragmentMethod =
            typeof(MatchesSqlExtensions).GetMethod(nameof(MatchesSqlExtensions.MatchesSql), new[] { typeof(object), typeof(ISqlFragment) });

        public bool Matches(MethodCallExpression expression)
        {
            return Equals(expression.Method, _sqlMethod) || Equals(expression.Method, _fragmentMethod);
        }

        public ISqlFragment Parse(IFieldMapping mapping, ISerializer serializer, MethodCallExpression expression)
        {
            if (expression.Method.Equals(_sqlMethod))
                return new WhereFragment(expression.Arguments[1].Value().As<string>(), expression.Arguments[2].Value().As<object[]>());

            if (expression.Method.Equals(_fragmentMethod))
                return expression.Arguments[1].Value() as ISqlFragment;

            return null;
        }
    }
}
