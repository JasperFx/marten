using System;
using System.Reflection;
using JasperFx.Core.Reflection;

namespace Marten.Linq.Parsing.Methods.Strings;

internal class StringEndsWith: StringComparisonParser
{
    private static readonly StringComparison[] CaseInsensitiveComparisons =
    {
        StringComparison.OrdinalIgnoreCase, StringComparison.CurrentCultureIgnoreCase
    };

    public StringEndsWith(): base(
        ReflectionHelper.GetMethod<string>(s => s.EndsWith(null)),
        ReflectionHelper.GetMethod<string>(s => s.EndsWith(null, StringComparison.CurrentCulture))
    )
    {
    }

    public override string FormatValue(MethodInfo method, string value)
    {
        return "%" + value;
    }
}
