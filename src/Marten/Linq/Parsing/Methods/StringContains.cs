using System;
using System.Linq;
using System.Reflection;
using Baseline.Reflection;
using Marten.Util;

namespace Marten.Linq.Parsing.Methods
{
    internal class StringContains: StringComparisonParser
    {
        public StringContains() : base(GetContainsMethods())
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
                typeof(string).GetMethod("Contains", new Type[] { typeof(string), typeof(StringComparison)}),
                ReflectionHelper.GetMethod<string>(s => s.Contains(null)),
#if NET5_0
                ReflectionHelper.GetMethod<string>(s => s.Contains(null, StringComparison.CurrentCulture))
#endif
            }
            .Where(m => m != null)
            .Distinct()
            .ToArray();
        }
    }
}
