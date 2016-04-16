using System;
using System.Collections.Generic;
using Baseline;

namespace Marten
{
    public static class JsonExtensions
    {
        public static string Json<T>(this T target)
        {
            throw new NotImplementedException();
        }

        public static string ToJsonArray(this IEnumerable<string> strings)
        {
            return $"[{strings.Join(",")}]";
        }
    }
}