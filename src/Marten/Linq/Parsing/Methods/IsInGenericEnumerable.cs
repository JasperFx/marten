using System.Linq;
using System.Linq.Expressions;
using JasperFx.Core.Reflection;
using Marten.Linq.Fields;
using Marten.Linq.QueryHandlers;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing.Methods;

internal class IsInGenericEnumerable: IMethodCallParser
{
    public bool Matches(MethodCallExpression expression)
    {
        return expression.Method.Name == LinqConstants.CONTAINS &&
               expression.Object.Type.IsGenericEnumerable() &&
               !expression.Arguments.Single().IsValueExpression();
    }

    public ISqlFragment Parse(IFieldMapping mapping, IReadOnlyStoreOptions options, MethodCallExpression expression)
    {
        var locator = mapping.FieldFor(expression).TypedLocator;
        var values = expression.Object.Value();

        return new WhereFragment($"{locator} = ANY(?)", values);
    }
}
