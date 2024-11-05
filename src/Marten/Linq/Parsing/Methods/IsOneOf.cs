#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using JasperFx.Core.Reflection;
using Marten.Linq.Members;
using Marten.Linq.SqlGeneration.Filters;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing.Methods;

internal class IsOneOf: IMethodCallParser
{
    public bool Matches(MethodCallExpression expression)
    {
        return (expression.Method.Name == nameof(LinqExtensions.IsOneOf)
                || (expression.Method.Name == nameof(LinqExtensions.In) &&
                    !expression.Arguments.First().Type.IsGenericEnumerable()))
               && expression.Method.DeclaringType == typeof(LinqExtensions);
    }

    public ISqlFragment Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        var queryableMember = memberCollection.MemberFor(expression.Arguments[0]);
        var locator = queryableMember.TypedLocator;
        var values = expression.Arguments[1].ReduceToConstant().Value!;

        if (queryableMember.MemberType.IsEnum)
        {
            return new EnumIsOneOfWhereFragment(values, options.Serializer().EnumStorage, locator);
        }
        else if (queryableMember is IValueTypeMember valueTypeMember)
        {
            return new IsOneOfFilter(queryableMember, new CommandParameter(valueTypeMember.ConvertFromWrapperArray(values)));
        }

        return new IsOneOfFilter(queryableMember, new CommandParameter(values));
    }
}

internal class IsOneOfFilter: ISqlFragment
{
    private readonly ISqlFragment _member;
    private readonly CommandParameter _parameter;

    public IsOneOfFilter(ISqlFragment member, CommandParameter parameter)
    {
        _member = member;
        _parameter = parameter;
    }

    public void Apply(IPostgresqlCommandBuilder builder)
    {
        _member.Apply(builder);
        builder.Append(" = ANY(");
        _parameter.Apply(builder);
        builder.Append(')');
    }

}
