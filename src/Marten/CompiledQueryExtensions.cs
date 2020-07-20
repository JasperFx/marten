using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;

namespace Marten
{
    public static class CompiledQueryExtensions
    {
        public static string AsJson<T>(this T target)
        {
            throw new NotSupportedException();
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
            return queryable.As<IMartenQueryable<T>>().ToJsonArray();
        }

        public static string ToJsonArray<T>(this IOrderedQueryable<T> queryable)
        {
            return queryable.As<IMartenQueryable<T>>().ToJsonArray();
        }

        public static Task<string> ToJsonArrayAsync<T>(this IQueryable<T> queryable, CancellationToken token = default)
        {
            return queryable.As<IMartenQueryable<T>>().ToJsonArrayAsync(token);
        }
    }
}
