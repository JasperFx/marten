using System.Diagnostics;
using System.Linq;
using Baseline;
using Marten.Linq;
using Marten.Linq.Compiled;

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
        public static string TransformToJson<T>(this T doc, string transformName)
        {
            return "";
        }

        public static IQueryable<string> TransformToJson<T>(this IQueryable<T> queryable, string transformName)
        {
            return queryable.Select(x => x.TransformToJson(transformName));
        }

        public static IQueryable<T> TransformTo<T>(this IQueryable queryable, string transformName)
        {
            return queryable.As<IMartenQueryable>().TransformTo<T>(transformName);
        }

        public static TDoc TransformTo<T, TDoc>(this T doc, string transformName)
        {
            return default(TDoc);
        }
    }
}