using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
#nullable enable
namespace Marten
{
    public static class CompiledQueryExtensions
    {
        /// <summary>
        /// Marks a compiled query as returning JSON
        /// </summary>
        /// <param name="target"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        /// <exception cref="NotSupportedException"></exception>
        internal static string AsJson<T>(this T target)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Return the raw stored JSON for this Linq query
        /// </summary>
        /// <param name="queryable"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static IQueryable<string> AsJson<T>(this IMartenQueryable<T> queryable)
        {
            return queryable.Select(x => x.AsJson());
        }

        /// <summary>
        /// Return the raw stored JSON for this Linq query
        /// </summary>
        /// <param name="queryable"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static IQueryable<string> AsJson<T>(this IQueryable<T> queryable)
        {
            return queryable.Select(x => x.AsJson());
        }

        /// <summary>
        /// Return the raw stored JSON for this Linq query
        /// </summary>
        /// <param name="queryable"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static IQueryable<string> AsJson<T>(this IOrderedQueryable<T> queryable)
        {
            return queryable.Select(x => x.AsJson());
        }

        /// <summary>
        /// Return the raw stored results as a single JSON array string for this Linq query
        /// </summary>
        /// <param name="queryable"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static string ToJsonArray<T>(this IQueryable<T> queryable)
        {
            return queryable.As<IMartenQueryable<T>>().ToJsonArray();
        }

        /// <summary>
        /// Return the raw stored results as a single JSON array string for this Linq query
        /// </summary>
        /// <param name="queryable"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static string ToJsonArray<T>(this IOrderedQueryable<T> queryable)
        {
            return queryable.As<IMartenQueryable<T>>().ToJsonArray();
        }

        /// <summary>
        /// Return the raw stored results as a single JSON array string for this Linq query
        /// </summary>
        /// <param name="queryable"></param>
        /// <param name="token"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static Task<string> ToJsonArrayAsync<T>(this IQueryable<T> queryable, CancellationToken token = default)
        {
            return queryable.As<IMartenQueryable<T>>().ToJsonArrayAsync(token);
        }
    }
}
