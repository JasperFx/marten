using System;
using System.Reflection;
using Baseline.Reflection;

namespace Marten.Linq.Handlers
{
    public class StringEquals : StringComparisonParser
    {
        public StringEquals() : base(
            ReflectionHelper.GetMethod<string>(s => s.Equals(string.Empty)),
            ReflectionHelper.GetMethod<string>(s => s.Equals(string.Empty, StringComparison.CurrentCulture)),
            ReflectionHelper.GetMethod(() => string.Equals(string.Empty, string.Empty)),
            ReflectionHelper.GetMethod(() => string.Equals(string.Empty, string.Empty, StringComparison.CurrentCulture)))
        {
        }

        protected override string FormatValue(MethodInfo method, string value)
        {
            return value;
        }
    }
}