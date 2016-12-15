using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Baseline;

namespace Marten.Pagination
{
    /// <summary>
    /// Extension methods on <see cref="IQueryable{T}"/> for performing paged queries
    /// </summary>
    public static class PagedQueryableExtensions
    {
        /// <summary>
        /// Extension method to return a paged results
        /// </summary>
        /// <typeparam name="T">Document Type</typeparam>
        /// <param name="queryable">Extension point on <see cref="IQueryable{T}"/></param>
        /// <param name="pageNumber">Page number</param>
        /// <param name="pageSize">Page size</param>
        /// <param name="order">Order clause</param>
        /// <returns>return paged result</returns>
        public static PagedList<T> PagedQuery<T>(
            this IQueryable<T> queryable, 
            int pageNumber, 
            int pageSize, 
            Func<IQueryable<T>, IQueryable<T>> order = null)
        {
            // throw an argument exception if page number is less than one
            if (pageNumber < 1)
            {
                throw new ArgumentOutOfRangeException($"pageNumber = {pageNumber}. PageNumber cannot be below 1.");
            }

            // throw argument exception if page size is less than one
            if (pageSize < 1)
            {
                throw new ArgumentOutOfRangeException($"pageSize = {pageSize}. PageSize cannot be less than 1.");
            }

            // get the total records which will help to compute the number of pages
            var totalItemCount = queryable.Count();

            // apply order if passed
            if (order != null)
            {
                queryable = order(queryable);
            }

            // compute the number of items to be skipped
            var skipCount = pageNumber > 0 ? (pageNumber - 1) * pageSize : 0;

            IEnumerable<T> items = null;

            // get paged result
            if (totalItemCount > 0)
            {
                items = queryable.Skip(skipCount).Take(pageSize).AsEnumerable();
            }

            // return paged list
            return new PagedList<T>(pageNumber, pageSize, totalItemCount, items);
        }

        /// <summary>
        /// Async Extension method to return a paged results
        /// </summary>
        /// <typeparam name="T">Document Type</typeparam>
        /// <param name="queryable">Extension point on <see cref="IQueryable{T}"/></param>
        /// <param name="pageNumber">Page number</param>
        /// <param name="pageSize">Page size</param>
        /// <param name="order">Order clause</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>return paged result</returns>
        public static async Task<PagedList<T>> PagedQueryAsync<T>(
            this IQueryable<T> queryable, 
            int pageNumber, 
            int pageSize, 
            Func<IQueryable<T>, IQueryable<T>> order = null, 
            CancellationToken token = default(CancellationToken))
        {
            // throw an argument exception if page number is less than one
            if (pageNumber < 1)
            {
                throw new ArgumentOutOfRangeException($"pageNumber = {pageNumber}. PageNumber cannot be below 1.");
            }

            // throw argument exception if page size is less than one
            if (pageSize < 1)
            {
                throw new ArgumentOutOfRangeException($"pageSize = {pageSize}. PageSize cannot be less than 1.");
            }

            // get the total records which will help to compute the number of pages    
            var totalItemCount = await queryable.CountAsync(token).ConfigureAwait(false);

            // apply order if passed
            if (order != null)
            {
                queryable = order(queryable);
            }

            // compute the number of items to be skipped
            var skipCount = pageNumber > 0 ? (pageNumber - 1) * pageSize : 0;

            IEnumerable<T> items = null;

            // get paged result
            if (totalItemCount > 0)
            {
                items = await queryable.Skip(skipCount).Take(pageSize).As<IMartenQueryable>().ToListAsync<T>(token);
            }

            // return paged list
            return new PagedList<T>(pageNumber, pageSize, totalItemCount, items);
        }
    }
}