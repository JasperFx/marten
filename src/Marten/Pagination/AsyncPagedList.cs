using System;
using System.Linq;
using System.Threading.Tasks;

namespace Marten.Pagination
{
    /// <summary>
    /// Class to return The async paged list from a paged query.
    /// </summary>
    /// <typeparam name="T">Document Type</typeparam>
    public class AsyncPagedList<T> : BasePagedList<T>
    {
        private AsyncPagedList()
        {
        }

        /// <summary>
        /// Async static method to create a new instance of the <see cref="AsyncPagedList{T}
        /// </summary>
        /// <param name="superset"></param>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <returns></returns>
        public static async Task<AsyncPagedList<T>> CreateAsync(IQueryable<T> superset, int pageNumber, int pageSize)
        {
            var pagedList = new AsyncPagedList<T>();
            await pagedList.InitAsync(superset, pageNumber, pageSize).ConfigureAwait(false);
            return pagedList;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncPagedList{T}" /> class.
        /// </summary>
        /// <param name="subset">Query for which data has to be fetched</param>
        /// <param name="pageSize">Page size</param>
        /// <param name="totalItemCount">Total count of all records</param>
        public async Task InitAsync(IQueryable<T> superset, int pageNumber, int pageSize)
        {
            // throw an argument exception if page number is less than one
            if (pageNumber < 1)
            {
                throw new ArgumentOutOfRangeException($"pageNumber = {pageNumber}. PageNumber cannot be below 1.");
            }

            // throw an argument exception if page Size is less than one
            if (pageSize < 1)
            {
                throw new ArgumentOutOfRangeException($"pageSize = {pageSize}. PageSize cannot be below 1.");
            }

            // fetch the total record count
            var totalItemCount = superset == null ? 0 : await superset.CountAsync().ConfigureAwait(false);

            // if there are zero records then don't proceed further
            if (totalItemCount == 0)
            {
                return;
            }

            // compute the number of pages based on page size and total records
            var pageCount = totalItemCount > 0 ? (int)Math.Ceiling(totalItemCount / (double)pageSize) : 0;

            // throw argument exception if page count is outside the page count range
            if (pageNumber > pageCount)
            {
                throw new ArgumentOutOfRangeException($"pageNumber = {pageNumber}. PageNumber is the outside the valid range.");
            }

            PageNumber = pageNumber;
            PageSize = pageSize;
            PageCount = pageCount;
            TotalItemCount = totalItemCount;

            // compute if there is a previous page
            HasPreviousPage = PageNumber > 1;

            // compute if there is next page
            HasNextPage = PageNumber < PageCount;

            // compute if the current page is first page
            IsFirstPage = PageNumber == 1;

            // compute if the current page is last page
            IsLastPage = PageNumber >= PageCount;

            // compute one-based index of first item on a specific page 
            FirstItemOnPage = ((PageNumber - 1) * PageSize) + 1;

            // compute one-based index of last item on a specific page
            var numberOfLastItemOnPage = FirstItemOnPage + PageSize - 1;
            LastItemOnPage = numberOfLastItemOnPage > TotalItemCount ? TotalItemCount : numberOfLastItemOnPage;

            // add items to internal list
            if (superset != null && TotalItemCount > 0)
            {
                if (pageNumber == 1)
                {
                    _subset.AddRange(await superset.Take<T>(pageSize).ToListAsync<T>().ConfigureAwait(false));
                }
                else
                {
                    var skipCount = (pageNumber - 1) * pageSize;
                    _subset.AddRange(await superset.Skip<T>(skipCount).Take<T>(pageSize).ToListAsync<T>().ConfigureAwait(false));
                }
            }
        }
    }
}