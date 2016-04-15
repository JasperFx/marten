using System;
using System.Reflection;
using Baseline.Reflection;
using Marten.Util;

namespace Marten.Linq.Parsing
{
    public class StringContains : StringComparisonParser
    {
        public StringContains() : base(
            ReflectionHelper.GetMethod<string>(s => s.Contains(null)),
            ReflectionHelper.GetMethod<string>(s => s.Contains(null, StringComparison.CurrentCulture)))
        {
        }

        protected override string FormatValue(MethodInfo method, string value)
        {
            return "%" + value + "%";
        }
    }
}