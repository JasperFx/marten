using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Marten.Pagination
{
    public class PagedListMetaData
    {
        public PagedListMetaData(int pageNumber, int pageSize, int totalItemCount)
        {
            TotalItemCount = totalItemCount;
            this.PageSize = pageSize;
            PageNumber = pageNumber;
            PageCount = TotalItemCount > 0
                            ? (int)Math.Ceiling(TotalItemCount / (double)PageSize)
                            : 0;
            HasPreviousPage = PageNumber > 1;
            HasNextPage = PageNumber < PageCount;
            IsFirstPage = PageNumber == 1;
            IsLastPage = PageNumber >= PageCount;
            FirstItemOnPage = (PageNumber - 1) * PageSize + 1;
            var numberOfLastItemOnPage = FirstItemOnPage + PageSize - 1;
            LastItemOnPage = numberOfLastItemOnPage > TotalItemCount
                                ? TotalItemCount
                                : numberOfLastItemOnPage;
        }

        public int PageNumber { get; private set; }

        public int PageSize { get; private set; }

        public int PageCount { get; private set; }

        public int TotalItemCount { get; private set; }

        public bool HasPreviousPage { get; private set; }
        
        public bool HasNextPage { get; private set; }

        public bool IsFirstPage { get; private set; }

        public bool IsLastPage { get; private set; }

        public int FirstItemOnPage { get; private set; }

        public int LastItemOnPage { get; private set; }
    }
}
