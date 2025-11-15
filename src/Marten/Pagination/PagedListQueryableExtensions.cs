#nullable enable
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;

namespace Marten.Pagination;

/// <summary>
///     Extension methods on <see cref="IMartenQueryable{T}" /> for performing paged queries
/// </summary>
public static class PagedListQueryableExtensions
{
    /// <summary>
    ///     Extension method to return a paged results
    /// </summary>
    /// <typeparam name="T">Document Type</typeparam>
    /// <param name="queryable">Extension point on <see cref="IQueryable{T}" /></param>
    /// <param name="pageNumber">one based page number</param>
    /// <param name="pageSize">Page size</param>
    /// <returns>return paged result</returns>
    public static IPagedList<T> ToPagedList<T>(
        this IQueryable<T> queryable,
        int pageNumber,
        int pageSize)
    {
        // return paged list
        return PagedList<T>.Create(queryable, pageNumber, pageSize, false);
    }

    /// <summary>
    ///     Extension method to return a paged results using separate count query
    /// </summary>
    /// <typeparam name="T">Document Type</typeparam>
    /// <param name="queryable">Extension point on <see cref="IQueryable{T}" /></param>
    /// <param name="pageNumber">one based page number</param>
    /// <param name="pageSize">Page size</param>
    /// <param name="useCountQuery">Use a separate count query rather than using Stats.</param>
    /// <returns>return paged result</returns>
    public static IPagedList<T> ToPagedList<T>(
        this IQueryable<T> queryable,
        int pageNumber,
        int pageSize, bool useQueryCount)
    {
        // return paged list
        return PagedList<T>.Create(queryable, pageNumber, pageSize, useQueryCount);
    }

    /// <summary>
    ///     Async Extension method to return a paged results
    /// </summary>
    /// <typeparam name="T">Document Type</typeparam>
    /// <param name="queryable">Extension point on <see cref="IQueryable{T}" /></param>
    /// <param name="pageNumber">One based page number</param>
    /// <param name="pageSize">Page size</param>
    /// <param name="token">Cancellation token</param>
    /// <returns>return paged result</returns>
    public static async Task<IPagedList<T>> ToPagedListAsync<T>(
        this IQueryable<T> queryable,
        int pageNumber,
        int pageSize,
        CancellationToken token = default)
    {
        // return paged list
        return await PagedList<T>.CreateAsync(queryable, pageNumber, pageSize, false, token).ConfigureAwait(false);
    }

    /// <summary>
    ///     Async Extension method to return a paged results
    /// </summary>
    /// <typeparam name="T">Document Type</typeparam>
    /// <param name="queryable">Extension point on <see cref="IQueryable{T}" /></param>
    /// <param name="pageNumber">One based page number</param>
    /// <param name="pageSize">Page size</param>
    /// <param name="token">Cancellation token</param>
    /// <param name="useCountQuery">Use a separate count query rather than using Stats.</param>
    /// <returns>return paged result</returns>
    public static async Task<IPagedList<T>> ToPagedListAsync<T>(
        this IQueryable<T> queryable,
        int pageNumber,
        int pageSize,
        bool useCountQuery,
        CancellationToken token = default)
    {
        // return paged list
        return await PagedList<T>.CreateAsync(queryable, pageNumber, pageSize, useCountQuery, token).ConfigureAwait(false);
    }
}
