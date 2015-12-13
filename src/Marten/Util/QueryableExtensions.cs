using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;

namespace Marten.Util
{
    public static class QueryableExtensions
    {
        public static async Task<List<T>> ToListAsync<T>(this IQueryable<T> queryable, CancellationToken token = default(CancellationToken))
        {
            var martenQueryable = queryable as IMartenQueryable<T>;
            if (martenQueryable == null)
            {
                throw new InvalidOperationException($"{typeof(T)} is not IMartenQueryable<>");
            }

            var enumerable = await martenQueryable.ExecuteCollectionAsync(token).ConfigureAwait(false);
            return enumerable.ToList();
        }
    }
}