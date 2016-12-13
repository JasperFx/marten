using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Baseline;

namespace Marten.Pagination
{
    public static class PagedQueryableExtensions
    {
        public static PagedList<T> PagedQuery<T>(this IQueryable<T> queryable, int pageNumber, int pageSize, Func<IQueryable<T>, IQueryable<T>> order = null)
        {
            if (pageNumber < 1)
                throw new ArgumentOutOfRangeException($"pageNumber = {pageNumber}. PageNumber cannot be below 1.");
            if (pageSize < 1)
                throw new ArgumentOutOfRangeException($"pageSize = {pageSize}. PageSize cannot be less than 1.");

            var totalItemCount = queryable.Count();


            if (order != null)
            {
                queryable = order(queryable);
            }

            var skipCount = pageNumber > 0 ? (pageNumber - 1) * pageSize : 0;

            IEnumerable<T> items = null;

            if (totalItemCount > 0)
            {
                items = queryable.Skip(skipCount).Take(pageSize).AsEnumerable();
            }

            return new PagedList<T>(pageNumber, pageSize, totalItemCount, items);
        }

        public static async Task<PagedList<T>> PagedQueryAsync<T>(
            this IQueryable<T> queryable,
            int pageNumber,
            int pageSize,
            Func<IQueryable<T>, IQueryable<T>> order = null,
            CancellationToken token = default(CancellationToken))
        {
            if (pageNumber < 1)
                throw new ArgumentOutOfRangeException($"pageNumber = {pageNumber}. PageNumber cannot be below 1.");
            if (pageSize < 1)
                throw new ArgumentOutOfRangeException($"pageSize = {pageSize}. PageSize cannot be less than 1.");

            int totalItemCount = await queryable.CountAsync().ConfigureAwait(false);

            if (order != null)
            {
                queryable = order(queryable);
            }

            var skipCount = pageNumber > 0 ? (pageNumber - 1) * pageSize : 0;

            IEnumerable<T> items = null;

            if (totalItemCount > 0)
            {
                items = await queryable.Skip(skipCount).Take(pageSize).As<IMartenQueryable>().ToListAsync<T>(token);
            }

            return new PagedList<T>(pageNumber, pageSize, totalItemCount, items);
        }
    }
}
