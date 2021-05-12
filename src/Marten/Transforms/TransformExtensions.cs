using System;
using System.Linq;
using Baseline;
using Marten.Linq;

namespace Marten.Transforms
{
    public static class TransformExtensions
    {
        /// <summary>
        ///     Placeholder for Linq expressions
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="doc"></param>
        /// <param name="transformName"></param>
        /// <returns></returns>
        [Obsolete("Replace these w/ more streaming instead")]
        public static string TransformToJson<T>(this T doc, string transformName)
        {
            return "";
        }

        /// <summary>
        /// Execute the query using a named Javascript transformation
        /// </summary>
        /// <param name="queryable"></param>
        /// <param name="transformName"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        [Obsolete("Replace these w/ more streaming instead")]
        public static IQueryable<string> TransformToJson<T>(this IQueryable<T> queryable, string transformName)
        {
            return queryable.Select(x => x.TransformToJson(transformName));
        }

        /// <summary>
        /// Execute the query using a named Javascript transformation
        /// </summary>
        /// <param name="queryable"></param>
        /// <param name="transformName"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        [Obsolete("Replace these w/ more streaming instead")]
        public static IQueryable<T> TransformTo<T>(this IQueryable queryable, string transformName)
        {
            return queryable.As<IMartenQueryable>().TransformTo<T>(transformName);
        }

        /// <summary>
        /// Execute the query with a named Javascript transformation
        /// </summary>
        /// <param name="doc"></param>
        /// <param name="transformName"></param>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="TDoc"></typeparam>
        /// <returns></returns>
        [Obsolete("Replace these w/ more streaming instead")]
        public static TDoc TransformTo<T, TDoc>(this T doc, string transformName)
        {
            return default;
        }
    }
}
