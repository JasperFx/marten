using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.Core.Reflection;

namespace Marten.Linq.Parsing.Methods;

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

    protected override bool IsCaseInsensitiveComparison(MethodCallExpression expression)
    {
        if (AreMethodsEqual(expression.Method,
                ReflectionHelper.GetMethod<string>(s => s.StartsWith(null, StringComparison.OrdinalIgnoreCase))))
        {
            if (expression.Arguments[1] is ConstantExpression constant && constant.Value is StringComparison comparison)
            {
                return CaseInsensitiveComparisons.Any(x => x == comparison);
            }
        }

        return base.IsCaseInsensitiveComparison(expression);
    }
}
