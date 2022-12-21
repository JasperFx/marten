using System.Linq;
using System.Linq.Expressions;
using JasperFx.Core.Reflection;
using Marten.Linq.Fields;
using Marten.Util;
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

    public ISqlFragment Parse(IFieldMapping mapping, IReadOnlyStoreOptions options, MethodCallExpression expression)
    {
        var members = FindMembers.Determine(expression);

        var locator = mapping.FieldFor(members).TypedLocator;
        var values = expression.Arguments.Last().Value();

        if (members.Last().GetMemberType().IsEnum)
        {
            return new EnumIsOneOfWhereFragment(values, options.Serializer().EnumStorage, locator);
        }

        return new WhereFragment($"{locator} = ANY(?)", values);
    }
}
