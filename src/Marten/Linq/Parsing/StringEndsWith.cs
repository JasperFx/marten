using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Baseline.Reflection;

namespace Marten.Linq.Parsing
{
    public class StringEndsWith : StringComparisonParser
    {
        public StringEndsWith() : base(
            ReflectionHelper.GetMethod<string>(s => s.EndsWith(null)),
            ReflectionHelper.GetMethod<string>(s => s.EndsWith(null, StringComparison.CurrentCulture))
#if NET46
            ,ReflectionHelper.GetMethod<string>(s => s.EndsWith(null, true, null))
#endif
            )
        {
        }

        public override string FormatValue(MethodInfo method, string value)
        {
            return "%" + value;
        }

#if NET46
        protected override bool IsCaseInsensitiveComparison(MethodCallExpression expression)
        {
            if (AreMethodsEqual(expression.Method, ReflectionHelper.GetMethod<string>(s => s.StartsWith(null, true, null))))
            {
                var constant = expression.Arguments[1] as ConstantExpression;
                if (constant != null && constant.Value is bool) return (bool) constant.Value;
            }
            return base.IsCaseInsensitiveComparison(expression);
        }
#else
        private static readonly StringComparison[] CaseInsensitiveComparisons = {StringComparison.OrdinalIgnoreCase, StringComparison.CurrentCultureIgnoreCase};

        protected override bool IsCaseInsensitiveComparison(MethodCallExpression expression)
        {
            if (AreMethodsEqual(expression.Method, ReflectionHelper.GetMethod<string>(s => s.StartsWith(null, StringComparison.OrdinalIgnoreCase))))
            {
                var constant = expression.Arguments[1] as ConstantExpression;
                if (constant != null && constant.Value is StringComparison)
                {
                    var comparison = (StringComparison) constant.Value;
                    return CaseInsensitiveComparisons.Any(x => x == comparison);
                }
            }
            return base.IsCaseInsensitiveComparison(expression);
        }
#endif
    }
}