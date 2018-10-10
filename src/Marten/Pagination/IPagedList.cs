using System.Collections.Generic;

namespace Marten.Pagination
{
    /// <summary>
    /// Interface for paged list
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IPagedList<out T>: IEnumerable<T>
    {
        /// <summary>
        /// Return the paged query result
        /// </summary>
        /// <param name="index">Index to fetch item from paged query result</param>
        /// <returns>/returns item from paged query result</returns>
        T this[int index] { get; }

        /// <summary>
        /// Return the number of records in the paged query result
        /// </summary>
        long Count { get; }

        /// <summary>
        /// Gets current page number
        /// </summary>
        long PageNumber { get; }

        /// <summary>
        /// Gets page size
        /// </summary>

        long PageSize { get; }
        /// <summary>
        /// Gets number of pages
        /// </summary>
        long PageCount { get; }

        /// <summary>
        /// Gets the total number records
        /// </summary>
        long TotalItemCount { get; }

        /// <summary>
        /// Gets a value indicating whether there is a previous page
        /// </summary>
        bool HasPreviousPage { get; }

        /// <summary>
        /// Gets a value indicating whether there is next page
        /// </summary>
        bool HasNextPage { get; }

        /// <summary>
        /// Gets a value indicating whether the current page is first page
        /// </summary>
        bool IsFirstPage { get; }

        /// <summary>
        /// Gets a value indicating whether the current page is last page
        /// </summary>
        bool IsLastPage { get; }

        /// <summary>
        /// Gets one-based index of first item in current page
        /// </summary>
        long FirstItemOnPage { get; }

        /// <summary>
        /// Gets one-based index of last item in current page
        /// </summary>
        long LastItemOnPage { get; }
    }
}
