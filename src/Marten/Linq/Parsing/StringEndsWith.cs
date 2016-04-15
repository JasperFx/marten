using System;
using System.Linq.Expressions;
using System.Reflection;
using Baseline.Reflection;

namespace Marten.Linq.Parsing
{
    public class StringEndsWith : StringComparisonParser
    {
        public StringEndsWith() : base(
            ReflectionHelper.GetMethod<string>(s => s.EndsWith(null)),
            ReflectionHelper.GetMethod<string>(s => s.EndsWith(null, StringComparison.CurrentCulture)),
            ReflectionHelper.GetMethod<string>(s => s.EndsWith(null, true, null)))
        {
        }

        protected override string FormatValue(MethodInfo method, string value)
        {
            return "%" + value;
        }

        protected override bool IsCaseInsensitiveComparison(MethodCallExpression expression)
        {
            if (AreMethodsEqual(expression.Method, ReflectionHelper.GetMethod<string>(s => s.StartsWith(null, true, null))))
            {
                var constant = expression.Arguments[1] as ConstantExpression;
                if (constant != null && constant.Value is bool) return (bool) constant.Value;
            }
            return base.IsCaseInsensitiveComparison(expression);
        }
    }
}