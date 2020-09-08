using System;
using System.Linq.Expressions;
using System.Reflection;
using Baseline.Reflection;
using Marten.Linq.Parsing.Methods;

namespace Marten.Linq.Parsing
{
    internal class StringStartsWith: StringComparisonParser
    {
        public StringStartsWith() : base(
            ReflectionHelper.GetMethod<string>(s => s.StartsWith(null)),
            ReflectionHelper.GetMethod<string>(s => s.StartsWith(null, StringComparison.CurrentCulture))
#if NET461
            , ReflectionHelper.GetMethod<string>(s => s.StartsWith(null, true, null))
#endif
            )
        {
        }

        public override string FormatValue(MethodInfo method, string value)
        {
            return value + "%";
        }

#if NET461

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
