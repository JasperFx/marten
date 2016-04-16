using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;

namespace Marten
{
    public static class JsonExtensions
    {
        public static string Json<T>(this T target)
        {
            throw new NotImplementedException();
        }

        public static IQueryable<string> Json<T>(this IQueryable<T> queryable)
        {
            return queryable.Select(x => x.Json());
        }

        public static string ToJsonArray(this IEnumerable<string> strings)
        {
            return $"[{strings.Join(",")}]";
        }
    }
}