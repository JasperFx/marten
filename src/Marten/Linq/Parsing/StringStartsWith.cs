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
        )
        {
        }

        public override string FormatValue(MethodInfo method, string value)
        {
            return value + "%";
        }
    }
}
