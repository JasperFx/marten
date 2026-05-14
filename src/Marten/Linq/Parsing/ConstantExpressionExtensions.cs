#nullable enable
using System.Linq.Expressions;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Linq.Parsing;

[UnconditionalSuppressMessage("Trimming", "IL2075",
    Justification = "Class-level: PublicMethods/PublicProperties access via a Type obtained from object.GetType() / GetGenericArguments. Source instance is preserved at the StoreOptions / projection-registration boundary.")]
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

        // Check if it is an F# Option (without compile-time FSharp.Core dependency)
        if (FSharpTypeHelper.IsFSharpOptionType(valueType))
        {
            // If we are here, 'value' is not null, so it MUST be 'Some'.
            // We can safely just grab the 'Value' property.
            var valueProp = valueType.GetProperty("Value");
            return valueProp?.GetValue(value);
        }

        return value;
    }
}
