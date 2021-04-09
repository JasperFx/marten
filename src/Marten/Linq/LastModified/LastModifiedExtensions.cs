using System;
#nullable enable
namespace Marten.Linq.LastModified
{
    public static class LastModifiedExtensions
    {
        /// <summary>
        /// The search results should include documents modified since given time (&gt;)
        /// </summary>
        /// <param name="doc"></param>
        /// <param name="time"></param>
        /// <returns></returns>
        public static bool ModifiedSince(this object doc, DateTimeOffset time)
        {
            return true;
        }

        /// <summary>
        /// The search results should include documents modified before given time (&lt;)
        /// </summary>
        /// <param name="doc"></param>
        /// <param name="time"></param>
        /// <returns></returns>
        public static bool ModifiedBefore(this object doc, DateTimeOffset time)
        {
            return true;
        }
    }
}
