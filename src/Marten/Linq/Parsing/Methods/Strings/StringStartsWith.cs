using System;
using System.Reflection;
using JasperFx.Core.Reflection;

namespace Marten.Linq.Parsing.Methods.Strings;

internal class StringStartsWith: StringComparisonParser
{
    public StringStartsWith(): base(
        ReflectionHelper.GetMethod<string>(s => s.StartsWith(null)),
        ReflectionHelper.GetMethod<string>(s => s.StartsWith(null, StringComparison.CurrentCulture))
    )
    {
    }

    public override string FormatValue(MethodInfo method, string value)
    {
        return value + "%";
    }
}
