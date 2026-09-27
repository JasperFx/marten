#nullable enable
using System.Linq;
using System.Linq.Expressions;
using JasperFx.Core.Reflection;
using JasperFx.MultiTenancy;
using Marten.Linq.Members;
using Marten.Linq.SqlGeneration.Filters;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing.Methods;

internal class TenantIsOneOf: IMethodCallParser
{
    public bool Matches(MethodCallExpression expression)
    {
        return expression.Method.Name == nameof(LinqExtensions.TenantIsOneOf)
               && expression.Method.DeclaringType == typeof(LinqExtensions);
    }

    public ISqlFragment Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        var values = expression.Arguments.Last().Value().As<string[]>();

        // #5516: the values are tenant ids supplied by the caller, so they get the same correction every
        // other tenant-id entry point applies. Raw, a mixed-case id under ForceLowerCase filtered on a
        // tenant_id that nothing writes, and the query simply returned nothing.
        // A lambda rather than a method group: MaybeCorrectTenantId extends the TenantIdStyle enum, and
        // an extension method on a value type cannot be used to create a delegate (CS1113).
        return new TenantIsOneOfFilter(
            values.Select(x => options.TenantIdStyle.MaybeCorrectTenantId(x)).ToArray());
    }
}
