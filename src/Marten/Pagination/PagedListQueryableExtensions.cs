using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Pagination
{
    /// <summary>
    /// Extension methods on <see cref="IQueryable{T}"/> for performing paged queries
    /// </summary>
    public static class PagedListQueryableExtensions
    {
        /// <summary>
        /// Extension method to return a paged results
        /// </summary>
        /// <typeparam name="T">Document Type</typeparam>
        /// <param name="queryable">Extension point on <see cref="IQueryable{T}"/></param>
        /// <param name="pageNumber">one based page number</param>
        /// <param name="pageSize">Page size</param>
        /// <returns>return paged result</returns>
        public static IPagedList<T> ToPagedList<T>(
            this IQueryable<T> queryable, 
            int pageNumber, 
            int pageSize)
        {
            // return paged list
            return new PagedList<T>(queryable, pageNumber, pageSize);
        }

        /// <summary>
        /// Async Extension method to return a paged results
        /// </summary>
        /// <typeparam name="T">Document Type</typeparam>
        /// <param name="queryable">Extension point on <see cref="IQueryable{T}"/></param>
        /// <param name="pageNumber">One based page number</param>
        /// <param name="pageSize">Page size</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>return paged result</returns>
        public static async Task<IPagedList<T>> ToPagedListAsync<T>(
            this IQueryable<T> queryable, 
            int pageNumber, 
            int pageSize, 
            CancellationToken token = default(CancellationToken))
        {
            // return paged list
            return await AsyncPagedList<T>.CreateAsync(queryable, pageNumber, pageSize);
        }
    }
}