using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Services.Includes;
using Marten.Util;

namespace Marten
{
    public static class CompiledQueryExtensions
    {
        public static string AsJson<T>(this T target)
        {
            throw new NotImplementedException();
        }

        public static IMartenQueryable<T> Include<T, TQuery>(this IQueryable<T> queryable, Expression<Func<T, object>> idSource, Func<TQuery,object> callback,
            JoinType joinType = JoinType.Inner)
        {
            throw new NotImplementedException();
            //return ((IMartenQueryable<T>)queryable).Include(idSource, callback);
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