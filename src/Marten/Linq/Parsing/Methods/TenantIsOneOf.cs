using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Marten.Linq.Fields;
using Marten.Linq.Filters;
using Marten.Linq.SqlGeneration;

namespace Marten.Linq.Parsing.Methods
{
    internal class TenantIsOneOf: IMethodCallParser
    {
        public bool Matches(MethodCallExpression expression)
        {
            return expression.Method.Name == nameof(LinqExtensions.TenantIsOneOf)
                   && expression.Method.DeclaringType == typeof(LinqExtensions);
        }

        public ISqlFragment Parse(IFieldMapping mapping, ISerializer serializer, MethodCallExpression expression)
        {
            var values = expression.Arguments.Last().Value().As<string[]>();
            return new TenantIsOneOfWhereFragment(values);
        }
    }
}