using System.Collections.Generic;
using System.Linq;
using Baseline;

namespace Marten
{
    public static class LinqExtensions
    {
        /// <summary>
        /// Used for Linq queries to match an element to one of a list of values
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="variable"></param>
        /// <param name="matches"></param>
        /// <returns></returns>
        public static bool IsOneOf<T>(this T variable, params T[] matches)
        {
            return matches.Contains(variable);
        }

        /// <summary>
        /// Used for Linq queries to determines whether an element is a superset of the specified collection
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="enumerable"></param>
        /// <param name="items"></param>
        /// <returns></returns>
        public static bool IsSupersetOf<T>(this IEnumerable<T> enumerable, params T[] items)
        {
            var hashSet = new HashSet<T>(enumerable);
            return hashSet.IsSupersetOf(items);
        }

        /// <summary>
        /// Used for Linq queries to determines whether an element is a superset of the specified collection
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="enumerable"></param>
        /// <param name="items"></param>
        /// <returns></returns>
        public static bool IsSupersetOf<T>(this IEnumerable<T> enumerable, IEnumerable<T> items)
        {
            var hashSet = new HashSet<T>(enumerable);
            return hashSet.IsSupersetOf(items);
        }

        /// <summary>
        /// Used for Linq queries to match on empty child collections
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="enumerable"></param>
        /// <returns></returns>
        public static bool IsEmpty<T>(this IEnumerable<T> enumerable)
        {
            if (enumerable == null) return true;

            if (enumerable is string)
            {
                return string.IsNullOrEmpty(enumerable.As<string>());
            }

            return !enumerable.Any();
        }

        /// <summary>
        /// Query across any and all tenants
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="variable"></param>
        /// <returns></returns>
        public static bool AnyTenant<T>(this T variable)
        {
            return true;
        }

        /// <summary>
        /// Query for the range of supplied tenants
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="variable"></param>
        /// <param name="tenantIds"></param>
        /// <returns></returns>
        public static bool TenantIsOneOf<T>(this T variable, params string[] tenantIds)
        {
            return true;
        }
    }
}