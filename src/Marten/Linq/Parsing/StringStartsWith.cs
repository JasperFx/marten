using System;
using System.Linq.Expressions;
using System.Reflection;
using Baseline.Reflection;

namespace Marten.Linq.Parsing
{
    public class StringStartsWith: StringComparisonParser
    {
        public StringStartsWith() : base(
            ReflectionHelper.GetMethod<string>(s => s.StartsWith(null)),
            ReflectionHelper.GetMethod<string>(s => s.StartsWith(null, StringComparison.CurrentCulture))
#if NET46
            , ReflectionHelper.GetMethod<string>(s => s.StartsWith(null, true, null))
#endif
            )
        {
        }

        public override string FormatValue(MethodInfo method, string value)
        {
            return value + "%";
        }

#if NET46

        protected override bool IsCaseInsensitiveComparison(MethodCallExpression expression)
        {
            if (AreMethodsEqual(expression.Method, ReflectionHelper.GetMethod<string>(s => s.StartsWith(null, true, null))))
            {
                var constant = expression.Arguments[1] as ConstantExpression;
                if (constant != null && constant.Value is bool)
                    return (bool)constant.Value;
            }
            return base.IsCaseInsensitiveComparison(expression);
        }

#endif
    }
}
