using System;
using System.Runtime.CompilerServices;

namespace Marten.Linq.SoftDeletes
{
    public static class SoftDeletedExtensions
    {
        /// <summary>
        /// The search results should include all documents, whether
        /// soft-deleted or not
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="doc"></param>
        /// <returns></returns>
        public static bool MaybeDeleted(this object doc)
        {
            return true;
        }

        /// <summary>
        /// The search results should only include soft-deleted
        /// documents
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="doc"></param>
        /// <returns></returns>
        public static bool IsDeleted(this object doc)
        {
            return true;
        }

        /// <summary>
        /// The search results should include documents deleted since given time (&gt;)
        /// </summary>
        /// <param name="doc"></param>
        /// <param name="time"></param>
        /// <returns></returns>
        public static bool DeletedSince(this object doc, DateTimeOffset time)
        {
            return true;
        }

        /// <summary>
        /// The search results should include documents deleted before given time (&lt;)
        /// </summary>
        /// <param name="doc"></param>
        /// <param name="time"></param>
        /// <returns></returns>
        public static bool DeletedBefore(this object doc, DateTimeOffset time)
        {
            return true;
        }
    }
}
