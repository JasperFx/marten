using System.Linq;
using System.Linq.Expressions;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Linq.Fields;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing.Methods;

internal class EqualsIgnoreCaseParser: IMethodCallParser
{
    public bool Matches(MethodCallExpression expression)
    {
        return expression.Method.Name == nameof(StringExtensions.EqualsIgnoreCase)
               && expression.Method.DeclaringType == typeof(StringExtensions);
    }

    public ISqlFragment Parse(IFieldMapping mapping, IReadOnlyStoreOptions options, MethodCallExpression expression)
    {
        var locator = mapping.FieldFor(expression).RawLocator;
        var value = expression.Arguments.Last().Value();

        return new WhereFragment($"{locator} ~~* ?", value.As<string>());
    }
}
