using System;

namespace Marten.Pagination
{
    public class PagedListMetaData
    {
        public PagedListMetaData(int pageNumber, int pageSize, int totalItemCount)
        {
            TotalItemCount = totalItemCount;
            PageSize = pageSize;
            PageNumber = pageNumber;

            // compute the nunber of pages based on page size and total records
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
        /// Gets current page number
        /// </summary>
        public int PageNumber { get; private set; }

        /// <summary>
        /// Gets page size
        /// </summary>
        public int PageSize { get; private set; }

        /// <summary>
        /// Gets number of pages
        /// </summary>
        public int PageCount { get; private set; }

        /// <summary>
        /// Gets the total number records
        /// </summary>
        public int TotalItemCount { get; private set; }

        /// <summary>
        /// Gets a value indicating whether there is a previous page
        /// </summary>
        public bool HasPreviousPage { get; private set; }
        
        /// <summary>
        /// Gets a value indicating whether there is next page
        /// </summary>
        public bool HasNextPage { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the current page is first page
        /// </summary>
        public bool IsFirstPage { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the current page is last page
        /// </summary>
        public bool IsLastPage { get; private set; }

        /// <summary>
        /// Gets one-based index of first item in current page
        /// </summary>
        public int FirstItemOnPage { get; private set; }

        /// <summary>
        /// Gets one-based index of last item in current page
        /// </summary>
        public int LastItemOnPage { get; private set; }
    }
}