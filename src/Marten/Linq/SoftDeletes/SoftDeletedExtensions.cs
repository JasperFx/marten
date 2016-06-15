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
    }
}