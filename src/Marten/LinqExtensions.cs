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
    }
}