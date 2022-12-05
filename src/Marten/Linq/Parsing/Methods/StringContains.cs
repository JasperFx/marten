using System;
using System.Linq;
using System.Reflection;
using JasperFx.Core.Reflection;

namespace Marten.Linq.Parsing.Methods;

internal class StringContains: StringComparisonParser
{
    public StringContains(): base(GetContainsMethods())
    {
    }

    public override string FormatValue(MethodInfo method, string value)
    {
        return "%" + value + "%";
    }

    private static MethodInfo[] GetContainsMethods()
    {
        return new[]
            {
                typeof(string).GetMethod("Contains", new[] { typeof(string), typeof(StringComparison) }),
                ReflectionHelper.GetMethod<string>(s => s.Contains(null)),
                ReflectionHelper.GetMethod<string>(s => s.Contains(null, StringComparison.CurrentCulture))
            }
            .Where(m => m != null)
            .Distinct()
            .ToArray();
    }
}
