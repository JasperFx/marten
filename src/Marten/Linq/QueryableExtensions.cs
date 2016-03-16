using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Marten.Linq
{
    public static class QueryableExtensions
    {
        public static NpgsqlCommand ToCommand<T>(this IQueryable<T> queryable, FetchType fetchType)
        {
            var q = queryable as MartenQueryable<T>;

            if (q == null)
            {
                throw new InvalidOperationException($"{nameof(ToCommand)} is only valid on Marten IQueryable objects");
            }

            return q.BuildCommand(fetchType);
        }

        public static string ToListJson<T>(this IQueryable<T> queryable)
        {
            var q = queryable as MartenQueryable<T>;

            if (q == null)
            {
                throw new InvalidOperationException($"{nameof(ToListJson)} is only valid on Marten IQueryable objects");
            }

            return q.ToListJson();
        }

        public static Task<string> ToListJsonAsync<T>(this IQueryable<T> queryable, CancellationToken token = default(CancellationToken))
        {
            var q = queryable as MartenQueryable<T>;

            if (q == null)
            {
                throw new InvalidOperationException($"{nameof(ToListJson)} is only valid on Marten IQueryable objects");
            }

            return q.ToListJsonAsync(token);
        }

        public static string FirstJson<T>(this IQueryable<T> queryable, Expression<Func<T,bool>> expression)
        {
            var q = queryable.Where(expression) as MartenQueryable<T>;

            if (q == null)
            {
                throw new InvalidOperationException($"{nameof(FirstOrDefaultJson)} is only valid on Marten IQueryable objects");
            }

            return q.FirstJson(false);
        }

        public static Task<string> FirstJsonAsync<T>(this IQueryable<T> queryable, Expression<Func<T,bool>> expression, CancellationToken token = default(CancellationToken))
        {
            var q = queryable.Where(expression) as MartenQueryable<T>;

            if (q == null)
            {
                throw new InvalidOperationException($"{nameof(FirstOrDefaultJson)} is only valid on Marten IQueryable objects");
            }

            return q.FirstJsonAsync(token, false);
        }

        public static string FirstOrDefaultJson<T>(this IQueryable<T> queryable, Expression<Func<T,bool>> expression)
        {
            var q = queryable.Where(expression) as MartenQueryable<T>;

            if (q == null)
            {
                throw new InvalidOperationException($"{nameof(FirstOrDefaultJson)} is only valid on Marten IQueryable objects");
            }

            return q.FirstJson(true);
        }

        public static Task<string> FirstOrDefaultJsonAsync<T>(this IQueryable<T> queryable, Expression<Func<T,bool>> expression, CancellationToken token = default(CancellationToken))
        {
            var q = queryable.Where(expression) as MartenQueryable<T>;

            if (q == null)
            {
                throw new InvalidOperationException($"{nameof(FirstOrDefaultJson)} is only valid on Marten IQueryable objects");
            }

            return q.FirstJsonAsync(token, true);
        }
    }
}