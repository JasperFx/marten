using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Marten.Pagination
{
    /// <summary>
    /// Base class for Paged list
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public abstract class BasePagedList<T>: IPagedList<T>
    {
        protected readonly List<T> _subset = new List<T>();

        protected internal BasePagedList()
        {
        }

        protected internal BasePagedList(int pageNumber, int pageSize, int totalItemCount)
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

            PageSize = pageSize;
            PageNumber = pageNumber;
            TotalItemCount = totalItemCount;

            // compute the number of pages based on page size and total records
            PageCount = TotalItemCount > 0 ? (int)Math.Ceiling(TotalItemCount / (double)PageSize) : 0;

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
        }

        /// <summary>
        /// Return the paged query result
        /// </summary>
        /// <param name="index">Index to fetch item from paged query result</param>
        /// <returns>/returns item from paged query result</returns>
        public T this[int index]
        {
            get { return _subset[index]; }
        }

        /// <summary>
        /// Return the number of records in the paged query result
        /// </summary>
        public int Count
        {
            get { return _subset.Count(); }
        }

        /// <summary>
        /// Generic Enumerator
        /// </summary>
        /// <returns>Generic Enumerator of paged query result</returns>
        public IEnumerator<T> GetEnumerator()
        {
            return ((IEnumerable<T>)_subset).GetEnumerator();
        }

        /// <summary>
        /// Enumerator
        /// </summary>
        /// <returns>Enumerator of paged query result</returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable<T>)_subset).GetEnumerator();
        }

        /// <summary>
        /// Gets current page number
        /// </summary>
        public int PageNumber { get; protected set; }

        /// <summary>
        /// Gets page size
        /// </summary>
        public int PageSize { get; protected set; }

        /// <summary>
        /// Gets number of pages
        /// </summary>
        public int PageCount { get; protected set; }

        /// <summary>
        /// Gets the total number records
        /// </summary>
        public int TotalItemCount { get; protected set; }

        /// <summary>
        /// Gets a value indicating whether there is a previous page
        /// </summary>
        public bool HasPreviousPage { get; protected set; }

        /// <summary>
        /// Gets a value indicating whether there is next page
        /// </summary>
        public bool HasNextPage { get; protected set; }

        /// <summary>
        /// Gets a value indicating whether the current page is first page
        /// </summary>
        public bool IsFirstPage { get; protected set; }

        /// <summary>
        /// Gets a value indicating whether the current page is last page
        /// </summary>
        public bool IsLastPage { get; protected set; }

        /// <summary>
        /// Gets one-based index of first item in current page
        /// </summary>
        public int FirstItemOnPage { get; protected set; }

        /// <summary>
        /// Gets one-based index of last item in current page
        /// </summary>
        public int LastItemOnPage { get; protected set; }
    }
}
