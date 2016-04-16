using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;

namespace Marten.Util
{
    public static class QueryableExtensions
    {

        public static QueryPlan Explain<T>(this IQueryable<T> queryable)
        {
            return queryable.As<IMartenQueryable<T>>().Explain();
        }


        #region ToListJson

        public static async Task<string> ToListJsonAsync<T>(this IQueryable<T> queryable,
            CancellationToken token = default(CancellationToken))
        {
            var raw = await queryable.Json().ToListAsync(token);

            return raw.ToJsonArray();
        }

        public static string ToListJson<T>(this IQueryable<T> queryable)
        {
            return queryable.Select(x => x.Json()).ToList().ToJsonArray();
        }

        #endregion

        #region ToList

        public static Task<IList<T>> ToListAsync<T>(this IQueryable<T> queryable,
            CancellationToken token = default(CancellationToken))
        {
            return queryable.As<IMartenQueryable>().ToListAsync<T>(token);
        }

        #endregion

        #region Any

        public static Task<bool> AnyAsync<TSource>(
            this IQueryable<TSource> source,
            CancellationToken token = default(CancellationToken))
        {
            if (source == null) throw new ArgumentNullException(nameof(source));


            return source.As<IMartenQueryable>().AnyAsync(token);
        }

        public static Task<bool> AnyAsync<TSource>(
            this IQueryable<TSource> source,
            Expression<Func<TSource, bool>> predicate,
            CancellationToken token = default(CancellationToken))
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return source.Where(predicate).AnyAsync(token);
        }

        #endregion

        #region Aggregate Functions

        public static Task<TResult> SumAsync<TSource, TResult>(
            this IQueryable<TSource> source, Expression<Func<TSource, TResult>> expression,
            CancellationToken token = default(CancellationToken))
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return source.Select(expression).As<IMartenQueryable>().SumAsync<TResult>(token);
        }


        public static Task<TResult> MaxAsync<TSource, TResult>(
            this IQueryable<TSource> source, Expression<Func<TSource, TResult>> expression,
            CancellationToken token = default(CancellationToken))
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return source.Select(expression).As<IMartenQueryable>().MaxAsync<TResult>(token);
        }


        public static Task<TResult> MinAsync<TSource, TResult>(
            this IQueryable<TSource> source, Expression<Func<TSource, TResult>> expression,
            CancellationToken token = default(CancellationToken))
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return source.Select(expression).As<IMartenQueryable>().MinAsync<TResult>(token);
        }

        public static Task<double> AverageAsync<TSource, TMember>(
            this IQueryable<TSource> source, Expression<Func<TSource, TMember>> expression,
            CancellationToken token = default(CancellationToken))
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return source.Select(expression).As<IMartenQueryable>().AverageAsync(token);
        }

        #endregion

        #region Count/LongCount/Sum

        public static Task<int> CountAsync<TSource>(
            this IQueryable<TSource> source,
            CancellationToken token = default(CancellationToken))
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return source.As<IMartenQueryable>().CountAsync(token);
        }


        public static Task<int> CountAsync<TSource>(
            this IQueryable<TSource> source,
            Expression<Func<TSource, bool>> predicate,
            CancellationToken token = default(CancellationToken))
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return source.Where(predicate).CountAsync(token);
        }


        public static Task<long> LongCountAsync<TSource>(
            this IQueryable<TSource> source,
            CancellationToken token = default(CancellationToken))
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return source.As<IMartenQueryable>().CountLongAsync(token);
        }

        public static Task<long> LongCountAsync<TSource>(
            this IQueryable<TSource> source,
            Expression<Func<TSource, bool>> predicate,
            CancellationToken token = default(CancellationToken))
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return source.Where(predicate).LongCountAsync(token);
        }

        #endregion

        #region First/FirstOrDefault

        public static Task<TSource> FirstAsync<TSource>(
            this IQueryable<TSource> source,
            CancellationToken token = default(CancellationToken))
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return source.As<IMartenQueryable>().FirstAsync<TSource>(token);
        }

        public static Task<string> FirstJsonAsync<TSource>(
            this IQueryable<TSource> source,
            CancellationToken token = default(CancellationToken))
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return source.Select(x => x.Json()).FirstAsync(token);
        }

        public static string FirstJson<TSource>(
            this IQueryable<TSource> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return source.Select(x => x.Json()).First();
        }


        public static Task<TSource> FirstAsync<TSource>(
            this IQueryable<TSource> source,
            Expression<Func<TSource, bool>> predicate,
            CancellationToken token = default(CancellationToken))
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return source.Where(predicate).FirstAsync(token);
        }

        public static Task<string> FirstJsonAsync<TSource>(
            this IQueryable<TSource> source,
            Expression<Func<TSource, bool>> predicate,
            CancellationToken token = default(CancellationToken))
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return source.Where(predicate).Select(x => x.Json()).FirstAsync(token);
        }

        public static string FirstJson<TSource>(
            this IQueryable<TSource> source,
            Expression<Func<TSource, bool>> predicate)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return source.Where(predicate).Json().First();
        }


        public static Task<TSource> FirstOrDefaultAsync<TSource>(
            this IQueryable<TSource> source,
            CancellationToken token = default(CancellationToken))
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return source.As<IMartenQueryable>().FirstOrDefaultAsync<TSource>(token);
        }

        public static Task<string> FirstOrDefaultJsonAsync<TSource>(
            this IQueryable<TSource> source,
            CancellationToken token = default(CancellationToken))
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return source.Select(x => x.Json()).FirstOrDefaultAsync(token);
        }


        public static string FirstOrDefaultJson<TSource>(
            this IQueryable<TSource> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return source.Select(x => x.Json()).FirstOrDefault();
        }


        public static Task<TSource> FirstOrDefaultAsync<TSource>(
            this IQueryable<TSource> source,
            Expression<Func<TSource, bool>> predicate,
            CancellationToken token = default(CancellationToken))
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return source.Where(predicate).FirstOrDefaultAsync(token);
        }

        public static Task<string> FirstOrDefaultJsonAsync<TSource>(
            this IQueryable<TSource> source,
            Expression<Func<TSource, bool>> predicate,
            CancellationToken token = default(CancellationToken))
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return source.Where(predicate).Json().FirstOrDefaultAsync(token);
        }

        public static string FirstOrDefaultJson<TSource>(
            this IQueryable<TSource> source,
            Expression<Func<TSource, bool>> predicate)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return source.Where(predicate).Json().FirstOrDefault();
        }

        #endregion

        #region Single/SingleOrDefault

        public static Task<TSource> SingleAsync<TSource>(
            this IQueryable<TSource> source,
            CancellationToken token = default(CancellationToken))
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return source.As<IMartenQueryable>().SingleAsync<TSource>(token);
        }

        public static Task<string> SingleJsonAsync<TSource>(
            this IQueryable<TSource> source,
            CancellationToken token = default(CancellationToken))
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return source.Select(x => x.Json()).SingleAsync(token);
        }

        public static string SingleJson<TSource>(
            this IQueryable<TSource> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return source.Select(x => x.Json()).Single();
        }

        public static Task<TSource> SingleAsync<TSource>(
            this IQueryable<TSource> source,
            Expression<Func<TSource, bool>> predicate,
            CancellationToken token = default(CancellationToken))
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return source.Where(predicate).SingleAsync(token);
        }

        public static Task<string> SingleJsonAsync<TSource>(
            this IQueryable<TSource> source,
            Expression<Func<TSource, bool>> predicate,
            CancellationToken token = default(CancellationToken))
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return source.Where(predicate).Json().SingleAsync(token);
        }

        public static string SingleJson<TSource>(
            this IQueryable<TSource> source,
            Expression<Func<TSource, bool>> predicate)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return source.Where(predicate).Json().Single();
        }


        public static Task<TSource> SingleOrDefaultAsync<TSource>(
            this IQueryable<TSource> source,
            CancellationToken token = default(CancellationToken))
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return source.As<IMartenQueryable>().SingleOrDefaultAsync<TSource>(token);
        }

        public static Task<string> SingleOrDefaultJsonAsync<TSource>(
            this IQueryable<TSource> source,
            CancellationToken token = default(CancellationToken))
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return source.Select(x => x.Json()).SingleOrDefaultAsync(token);
        }

        public static string SingleOrDefaultJson<TSource>(
            this IQueryable<TSource> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return source.Select(x => x.Json()).SingleOrDefault();
        }

        public static Task<TSource> SingleOrDefaultAsync<TSource>(
            this IQueryable<TSource> source,
            Expression<Func<TSource, bool>> predicate,
            CancellationToken token = default(CancellationToken))
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return source.Where(predicate).SingleOrDefaultAsync(token);
        }

        public static Task<string> SingleOrDefaultJsonAsync<TSource>(
            this IQueryable<TSource> source,
            Expression<Func<TSource, bool>> predicate,
            CancellationToken token = default(CancellationToken))
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return source.Where(predicate).Json().SingleOrDefaultAsync(token);
        }

        public static string SingleOrDefaultJson<TSource>(
            this IQueryable<TSource> source,
            Expression<Func<TSource, bool>> predicate)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));


            return source.Where(predicate).Json().SingleOrDefault();
        }

        #endregion

        #region Shared

        private static IMartenQueryable<T> CastToMartenQueryable<T>(IQueryable<T> queryable)
        {
            var martenQueryable = queryable as IMartenQueryable<T>;
            if (martenQueryable == null)
            {
                throw new InvalidOperationException($"{typeof (T)} is not IMartenQueryable<>");
            }

            return martenQueryable;
        }

        #endregion
    }
}