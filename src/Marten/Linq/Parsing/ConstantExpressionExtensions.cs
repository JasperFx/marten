#nullable enable
using System.Linq.Expressions;
using Microsoft.FSharp.Core;

namespace Marten.Linq.Parsing;

public static class ConstantExpressionExtensions
{
    public static object? UnwrapValue(this ConstantExpression constant)
    {
        var value = constant.Value;

        // If the value is null, it is either a C# null or FSharpOption.None.
        // In either case, there is nothing to unwrap.
        if (value == null)
        {
            return null;
        }

        var valueType = value.GetType();

        // Check if it is an F# Option
        // Note: FSharpOption is a class, not a struct.
        if (valueType.IsGenericType &&
            valueType.GetGenericTypeDefinition() == typeof(FSharpOption<>))
        {
            // If we are here, 'value' is not null, so it MUST be 'Some'.
            // We can safely just grab the 'Value' property.
            var valueProp = valueType.GetProperty("Value");
            return valueProp?.GetValue(value);
        }

        return value;
    }
}
