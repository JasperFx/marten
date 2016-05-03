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

        public static IQueryable<string> AsJson<T>(this IOrderedQueryable<T> queryable)
        {
            return queryable.Select(x => x.AsJson());
        }

        public static string ToJsonArray<T>(this IQueryable<T> queryable)
        {
            return $"[{queryable.Select(x=>x.AsJson()).ToArray().Join(",")}]";
        }

        public static string ToJsonArray<T>(this IOrderedQueryable<T> queryable)
        {
            return $"[{queryable.Select(x => x.AsJson()).ToArray().Join(",")}]";
        }

        public static async Task<string> ToJsonArrayAsync<T>(this IQueryable<T> queryable)
        {
            var jsonStrings = await queryable.Select(x=>x.AsJson()).ToListAsync().ConfigureAwait(false);
            return $"[{jsonStrings.Join(",")}]";
        }
    }
}