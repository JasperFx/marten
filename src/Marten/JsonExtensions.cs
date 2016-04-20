using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Util;

namespace Marten
{
    public static class JsonExtensions
    {
        public static string AsJson<T>(this T target)
        {
            throw new NotImplementedException();
        }

        public static IQueryable<string> AsJson<T>(this IMartenQueryable<T> queryable)
        {
            return queryable.Select(x => x.AsJson());
        }

        public static IQueryable<string> AsJson<T>(this IQueryable<T> queryable)
        {
            return queryable.Select(x => x.AsJson());
        }

        public static string ToJsonArray(this IEnumerable<string> strings)
        {
            return $"[{strings.Join(",")}]";
        }

        public async static Task<string> ToJsonArrayAsync(this IQueryable<string> strings)
        {
            var jsonStrings = await strings.ToListAsync();
            return $"[{jsonStrings.Join(",")}]";
        }
    }
}