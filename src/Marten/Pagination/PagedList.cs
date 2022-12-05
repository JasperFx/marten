#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Linq;

namespace Marten.Pagination;

/// <summary>
///     Class to return The async paged list from a paged query.
/// </summary>
/// <typeparam name="T">Document Type</typeparam>
public class PagedList<T>: IPagedList<T>
{
    private readonly List<T> _items = new();

    private PagedList()
    {
    }

    /// <summary>
    ///     Return the paged query result
    /// </summary>
    /// <param name="index">Index to fetch item from paged query result</param>
    /// <returns>/returns item from paged query result</returns>
    public T this[int index] => _items[index];

    /// <summary>
    ///     Return the number of records in the paged query result
    /// </summary>
    public long Count => _items.Count();

    /// <summary>
    ///     Generic Enumerator
    /// </summary>
    /// <returns>Generic Enumerator of paged query result</returns>
    public IEnumerator<T> GetEnumerator()
    {
        return ((IEnumerable<T>)_items).GetEnumerator();
    }

    /// <summary>
    ///     Enumerator
    /// </summary>
    /// <returns>Enumerator of paged query result</returns>
    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable<T>)_items).GetEnumerator();
    }

    /// <summary>
    ///     Gets current page number
    /// </summary>
    public long PageNumber { get; protected set; }

    /// <summary>
    ///     Gets page size
    /// </summary>
    public long PageSize { get; protected set; }

    /// <summary>
    ///     Gets number of pages
    /// </summary>
    public long PageCount { get; protected set; }

    /// <summary>
    ///     Gets the total number records
    /// </summary>
    public long TotalItemCount { get; protected set; }

    /// <summary>
    ///     Gets a value indicating whether there is a previous page
    /// </summary>
    public bool HasPreviousPage { get; protected set; }

    /// <summary>
    ///     Gets a value indicating whether there is next page
    /// </summary>
    public bool HasNextPage { get; protected set; }

    /// <summary>
    ///     Gets a value indicating whether the current page is first page
    /// </summary>
    public bool IsFirstPage { get; protected set; }

    /// <summary>
    ///     Gets a value indicating whether the current page is last page
    /// </summary>
    public bool IsLastPage { get; protected set; }

    /// <summary>
    ///     Gets one-based index of first item in current page
    /// </summary>
    public long FirstItemOnPage { get; protected set; }

    /// <summary>
    ///     Gets one-based index of last item in current page
    /// </summary>
    public long LastItemOnPage { get; protected set; }

    /// <summary>
    ///     Static method to create a new instance of the <see cref="PagedList{T}
    /// 
    /// </summary>
    /// <param name="queryable"></param>
    /// <param name="pageNumber"></param>
    /// <param name="pageSize"></param>
    public static PagedList<T> Create(IQueryable<T> queryable, int pageNumber, int pageSize)
    {
        var pagedList = new PagedList<T>();
        pagedList.Init(queryable, pageNumber, pageSize);
        return pagedList;
    }

    /// <summary>
    ///     Async static method to create a new instance of the <see cref="PagedList{T}
    /// 
    /// </summary>
    /// <param name="queryable"></param>
    /// <param name="pageNumber"></param>
    /// <param name="pageSize"></param>
    public static async Task<PagedList<T>> CreateAsync(IQueryable<T> queryable, int pageNumber, int pageSize,
        CancellationToken token = default)
    {
        var pagedList = new PagedList<T>();
        await pagedList.InitAsync(queryable, pageNumber, pageSize, token).ConfigureAwait(false);
        return pagedList;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="PagedList{T}" /> class.
    /// </summary>
    /// <param name="queryable">Query for which data has to be fetched</param>
    /// <param name="pageSize">Page size</param>
    /// <param name="totalItemCount">Total count of all records</param>
    public void Init(IQueryable<T> queryable, int pageNumber, int pageSize)
    {
        var query = PrepareQuery(queryable, pageNumber, pageSize, out var statistics);

        var items = query.ToList();

        ProcessResults(pageSize, items, statistics);
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="PagedList{T}" /> class.
    /// </summary>
    /// <param name="queryable">Query for which data has to be fetched</param>
    /// <param name="pageSize">Page size</param>
    /// <param name="totalItemCount">Total count of all records</param>
    public async Task InitAsync(IQueryable<T> queryable, int pageNumber, int pageSize,
        CancellationToken token = default)
    {
        var query = PrepareQuery(queryable, pageNumber, pageSize, out var statistics);

        var items = await query.ToListAsync(token).ConfigureAwait(false);

        ProcessResults(pageSize, items, statistics);
    }

    private IQueryable<T> PrepareQuery(IQueryable<T> queryable, int pageNumber, int pageSize,
        out QueryStatistics statistics)
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

        PageNumber = pageNumber;
        PageSize = pageSize;

        return pageNumber == 1
            ? queryable.As<IMartenQueryable<T>>().Stats(out statistics).Take(pageSize)
            : queryable.As<IMartenQueryable<T>>().Stats(out statistics).Skip((pageNumber - 1) * pageSize)
                .Take(pageSize);
    }

    private void ProcessResults(int pageSize, IReadOnlyList<T> items, QueryStatistics statistics)
    {
        _items.AddRange(items);

        // fetch the total record count
        TotalItemCount = statistics.TotalResults;

        // compute the number of pages based on page size and total records
        PageCount = TotalItemCount > 0 ? (int)Math.Ceiling(TotalItemCount / (double)pageSize) : 0;

        // compute if there is a previous page
        HasPreviousPage = PageNumber > 1;

        // compute if there is next page
        HasNextPage = PageNumber < PageCount;

        // compute if the current page is first page
        IsFirstPage = PageCount > 0 && PageNumber == 1;

        // compute if the current page is last page
        IsLastPage = PageCount > 0 && PageNumber >= PageCount;

        // compute one-based index of first item on a specific page
        FirstItemOnPage = PageCount > 0 ? ((PageNumber - 1) * PageSize) + 1 : 0;

        // compute one-based index of last item on a specific page
        var numberOfLastItemOnPage = FirstItemOnPage + PageSize - 1;
        LastItemOnPage = numberOfLastItemOnPage > TotalItemCount ? TotalItemCount : numberOfLastItemOnPage;
    }
}
