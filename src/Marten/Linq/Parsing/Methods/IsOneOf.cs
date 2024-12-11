#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using JasperFx.Core.Reflection;
using Marten.Linq.Members;
using Marten.Linq.SqlGeneration.Filters;
using Marten.Util;
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
        else if (queryableMember.IsGenericInterfaceImplementation(typeof(IValueTypeMember<,>)))
        {
            /* Unwrapping is required for nullable value types of the form: System.Nullable`1[ValueTypeTests.StrongTypedId.Issue2Id][]
             otherwise we get exceptions such as: Object of type 'System.Nullable`1[ValueTypeTests.StrongTypedId.Issue2Id][]' cannot be converted to type 'System.Collections.Generic.IEnumerable`1[ValueTypeTests.StrongTypedId.Issue2Id]'
             */
            var unwrappedValues = values.UnwrapIEnumerableOfNullables();
            var commandParameter = queryableMember.CallGenericInterfaceMethod(typeof(IValueTypeMember<,>), "ConvertFromWrapperArray", unwrappedValues);
            return new IsOneOfFilter(queryableMember, new CommandParameter(commandParameter));
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

    public void Apply(ICommandBuilder builder)
    {
        _member.Apply(builder);
        builder.Append(" = ANY(");
        _parameter.Apply(builder);
        builder.Append(')');
    }

}
