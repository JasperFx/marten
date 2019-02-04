using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Schema;

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
        /// Used for Linq queries to determines whether an element is a subset of the specified collection
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="enumerable"></param>
        /// <param name="items"></param>
        /// <returns></returns>
        public static bool IsSubsetOf<T>(this IEnumerable<T> enumerable, IEnumerable<T> items)
        {
            var hashSet = new HashSet<T>(enumerable);
            return hashSet.IsSubsetOf(items);
        }

        /// <summary>
        /// Used for Linq queries to determines whether an element is a subset of the specified collection
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="enumerable"></param>
        /// <param name="items"></param>
        /// <returns></returns>
        public static bool IsSubsetOf<T>(this IEnumerable<T> enumerable, params T[] items)
        {
            var hashSet = new HashSet<T>(enumerable);
            return hashSet.IsSubsetOf(items);
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

        /// <summary>
        /// Performs a full text search against <typeparamref name="TDoc"/>
        /// </summary>
        /// <param name="searchTerm">The text to search for.  May contain lexeme patterns used by PostgreSQL for full text searching</param>
        /// <remarks>
        /// See: https://www.postgresql.org/docs/10/static/textsearch-controls.html#TEXTSEARCH-PARSING-QUERIES
        /// </remarks>
        public static bool Search<T>(this T variable, string searchTerm)
        {
            return true;
        }

        /// <summary>
        /// Performs a full text search against <typeparamref name="TDoc"/>
        /// </summary>
        /// <param name="searchTerm">The text to search for.  May contain lexeme patterns used by PostgreSQL for full text searching</param>
        /// <param name="regConfig">The dictionary config passed to the 'to_tsquery' function, must match the config parameter used by <seealso cref="DocumentMapping.AddFullTextIndex(string)"/></param>
        /// <remarks>
        /// See: https://www.postgresql.org/docs/10/static/textsearch-controls.html#TEXTSEARCH-PARSING-QUERIES
        /// </remarks>
        public static bool Search<T>(this T variable, string searchTerm, string regConfig)
        {
            return true;
        }

        /// <summary>
        /// Performs a full text search against <typeparamref name="TDoc"/> using the 'plainto_tsquery' search function
        /// </summary>
        /// <param name="queryText">The text to search for.  May contain lexeme patterns used by PostgreSQL for full text searching</param>
        /// <remarks>
        /// See: https://www.postgresql.org/docs/10/static/textsearch-controls.html#TEXTSEARCH-PARSING-QUERIES
        /// </remarks>
        public static bool PlainTextSearch<T>(this T variable, string searchTerm)
        {
            return true;
        }

        /// <summary>
        /// Performs a full text search against <typeparamref name="TDoc"/> using the 'plainto_tsquery' search function
        /// </summary>
        /// <param name="queryText">The text to search for.  May contain lexeme patterns used by PostgreSQL for full text searching</param>
        /// <param name="regConfig">The dictionary config passed to the 'to_tsquery' function, must match the config parameter used by <seealso cref="DocumentMapping.AddFullTextIndex(string)"/></param>
        /// <remarks>
        /// See: https://www.postgresql.org/docs/10/static/textsearch-controls.html#TEXTSEARCH-PARSING-QUERIES
        /// </remarks>
        public static bool PlainTextSearch<T>(this T variable, string searchTerm, string regConfig)
        {
            return true;
        }

        /// <summary>
        /// Performs a full text search against <typeparamref name="TDoc"/> using the 'phraseto_tsquery' search function
        /// </summary>
        /// <param name="queryText">The text to search for.  May contain lexeme patterns used by PostgreSQL for full text searching</param>
        /// <remarks>
        /// See: https://www.postgresql.org/docs/10/static/textsearch-controls.html#TEXTSEARCH-PARSING-QUERIES
        /// </remarks>
        public static bool PhraseSearch<T>(this T variable, string searchTerm)
        {
            return true;
        }

        /// <summary>
        /// Performs a full text search against <typeparamref name="TDoc"/> using the 'phraseto_tsquery' search function
        /// </summary>
        /// <param name="queryText">The text to search for.  May contain lexeme patterns used by PostgreSQL for full text searching</param>
        /// <param name="regConfig">The dictionary config passed to the 'to_tsquery' function, must match the config parameter used by <seealso cref="DocumentMapping.AddFullTextIndex(string)"/></param>
        /// <remarks>
        /// See: https://www.postgresql.org/docs/10/static/textsearch-controls.html#TEXTSEARCH-PARSING-QUERIES
        /// </remarks>
        public static bool PhraseSearch<T>(this T variable, string searchTerm, string regConfig)
        {
            return true;
        }
    }
}