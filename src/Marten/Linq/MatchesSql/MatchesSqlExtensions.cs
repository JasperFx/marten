using System;

namespace Marten.Linq.MatchesSql
{
    public static class MatchesSqlExtensions
    {
        /// <summary>
        /// The search results should match the specified where fragment.
        /// </summary>
        /// <param name="doc"></param>
        /// <param name="whereFragment"></param>
        /// <returns></returns>
        public static bool MatchesSql(this object doc, IWhereFragment whereFragment)
        {
            throw new NotSupportedException($"{nameof(MatchesSql)} extension method can only be used in Marten Linq queries.");
        }

        /// <summary>
        /// The search results should match the specified raw sql fragment.
        /// </summary>
        /// <param name="doc"></param>
        /// <param name="sql"></param>
        /// <param name="parameters"></param>
        /// <returns></returns>
        public static bool MatchesSql(this object doc, string sql, params object[] parameters)
        {
            throw new NotSupportedException($"{nameof(MatchesSql)} extension method can only be used in Marten Linq queries.");
        }
    }
}
