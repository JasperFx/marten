using System.Linq.Expressions;
using Marten.Linq.Fields;
using Marten.Linq.Filters;
using Marten.Linq.SqlGeneration;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing.Methods
{
    internal class AnyTenant: WhereFragment, IMethodCallParser, ITenantWhereFragment
    {
        public AnyTenant() : base("1=1")
        {
        }

        public bool Matches(MethodCallExpression expression)
        {
            return expression.Method.Name == nameof(LinqExtensions.AnyTenant)
                   && expression.Method.DeclaringType == typeof(LinqExtensions);
        }

        public ISqlFragment Parse(IFieldMapping mapping, ISerializer serializer, MethodCallExpression expression)
        {
            return this;
        }
    }
}
