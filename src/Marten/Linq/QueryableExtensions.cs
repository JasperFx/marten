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
            var q = GetMartenQueryable(queryable, nameof(ToCommand));

            return q.BuildCommand(fetchType);
        }

        public static QueryPlan Explain<T>(this IQueryable<T> queryable)
        {
            var q = GetMartenQueryable(queryable, nameof(Explain));

            return q.Explain();
        }

        public static string ToListJson<T>(this IQueryable<T> queryable)
        {
            var q = GetMartenQueryable(queryable, nameof(ToListJson));

            return q.ToListJson();
        }

        public static Task<string> ToListJsonAsync<T>(this IQueryable<T> queryable, CancellationToken token = default(CancellationToken))
        {
            var q = GetMartenQueryable(queryable, nameof(ToListJsonAsync));

            return q.ToListJsonAsync(token);
        }

        public static string SingleJson<T>(this IQueryable<T> queryable, Expression<Func<T,bool>> predicate = null)
        {
            var q = GetMartenQueryable(queryable, predicate, nameof(SingleJson));

            return q.SingleJson(false);
        }

        public static Task<string> SingleJsonAsync<T>(this IQueryable<T> queryable, Expression<Func<T,bool>> predicate = null, CancellationToken token = default(CancellationToken))
        {
            var q = GetMartenQueryable(queryable, predicate, nameof(SingleJsonAsync));

            return q.SingleJsonAsync(false, token);
        }

        public static string SingleOrDefaultJson<T>(this IQueryable<T> queryable, Expression<Func<T,bool>> predicate = null)
        {
            var q = GetMartenQueryable(queryable, predicate, nameof(SingleOrDefaultJson));

            return q.SingleJson(true);
        }

        public static Task<string> SingleOrDefaultJsonAsync<T>(this IQueryable<T> queryable, Expression<Func<T, bool>> predicate = null, CancellationToken token = default(CancellationToken))
        {
            var q = GetMartenQueryable(queryable, predicate, nameof(SingleOrDefaultJsonAsync));

            return q.SingleJsonAsync(true, token);
        }

        public static string FirstJson<T>(this IQueryable<T> queryable, Expression<Func<T,bool>> predicate = null)
        {
            var q = GetMartenQueryable(queryable, predicate, nameof(FirstJson));

            return q.FirstJson(false);
        }

        public static Task<string> FirstJsonAsync<T>(this IQueryable<T> queryable, Expression<Func<T,bool>> predicate = null, CancellationToken token = default(CancellationToken))
        {
            var q = GetMartenQueryable(queryable, predicate, nameof(FirstJsonAsync));

            return q.FirstJsonAsync(false, token);
        }

        public static string FirstOrDefaultJson<T>(this IQueryable<T> queryable, Expression<Func<T,bool>> predicate = null)
        {
            var q = GetMartenQueryable(queryable, predicate, nameof(FirstOrDefaultJson));

            return q.FirstJson(true);
        }

        public static Task<string> FirstOrDefaultJsonAsync<T>(this IQueryable<T> queryable, Expression<Func<T,bool>> predicate = null, CancellationToken token = default(CancellationToken))
        {
            var q = GetMartenQueryable(queryable, predicate, nameof(FirstOrDefaultJsonAsync));

            return q.FirstJsonAsync(true, token);
        }

        private static MartenQueryable<T> GetMartenQueryable<T>(IQueryable<T> queryable, Expression<Func<T, bool>> predicate, string methodName)
        {
            var q = predicate != null ? queryable.Where(predicate) as MartenQueryable<T> : queryable as MartenQueryable<T>;

            if (q == null)
            {
                throw new InvalidOperationException($"{methodName} is only valid on Marten IQueryable objects");
            }
            return q;
        }

        private static MartenQueryable<T> GetMartenQueryable<T>(IQueryable<T> queryable, string methodName)
        {
            var q = queryable as MartenQueryable<T>;

            if (q == null)
            {
                throw new InvalidOperationException($"{methodName} is only valid on Marten IQueryable objects");
            }
            return q;
        }
    }
}