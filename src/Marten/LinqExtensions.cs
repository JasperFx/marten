using System.Collections.Generic;
using System.Linq;

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
    }
}