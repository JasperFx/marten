using System;
using System.Linq;
using Npgsql;

namespace Marten.Linq
{
    public static class QueryableExtensions
    {
        public static NpgsqlCommand ToCommand<T>(this IQueryable<T> queryable, FetchType fetchType = FetchType.FetchMany)
        {
            var q = queryable as MartenQueryable<T>;

            if (q == null)
            {
                throw new InvalidOperationException($"{nameof(ToCommand)} is only valid on Marten IQueryable objects");
            }

            return q.BuildCommand(fetchType);
        }
    }
}