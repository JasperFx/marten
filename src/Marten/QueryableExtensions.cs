using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Services.Includes;
using Npgsql;

namespace Marten
{
    public static class QueryableExtensions
    {
        public static QueryPlan Explain<T>(this IQueryable<T> queryable, Action<IConfigureExplainExpressions> configureExplain = null)
        {
            return queryable.As<IMartenQueryable<T>>().Explain(configureExplain: configureExplain);
        }

        #region ToList

        public static Task<IReadOnlyList<T>> ToListAsync<T>(this IQueryable<T> queryable,
            CancellationToken token = default(CancellationToken))
        {
            return queryable.As<IMartenQueryable>().ToListAsync<T>(token);
        }

        #endregion ToList

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

        #endregion Any

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

        #endregion Aggregate Functions

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

        #endregion Count/LongCount/Sum

        #region First/FirstOrDefault

        public static Task<TSource> FirstAsync<TSource>(
            this IQueryable<TSource> source,
            CancellationToken token = default(CancellationToken))
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return source.As<IMartenQueryable>().FirstAsync<TSource>(token);
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

        public static Task<TSource> FirstOrDefaultAsync<TSource>(
            this IQueryable<TSource> source,
            CancellationToken token = default(CancellationToken))
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return source.As<IMartenQueryable>().FirstOrDefaultAsync<TSource>(token);
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

        #endregion First/FirstOrDefault

        #region Single/SingleOrDefault

        public static Task<TSource> SingleAsync<TSource>(
            this IQueryable<TSource> source,
            CancellationToken token = default(CancellationToken))
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return source.As<IMartenQueryable>().SingleAsync<TSource>(token);
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

        public static Task<TSource> SingleOrDefaultAsync<TSource>(
            this IQueryable<TSource> source,
            CancellationToken token = default(CancellationToken))
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return source.As<IMartenQueryable>().SingleOrDefaultAsync<TSource>(token);
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

        #endregion Single/SingleOrDefault

        #region Shared

        private static IMartenQueryable<T> CastToMartenQueryable<T>(IQueryable<T> queryable)
        {
            var martenQueryable = queryable as IMartenQueryable<T>;
            if (martenQueryable == null)
            {
                throw new InvalidOperationException($"{typeof(T)} is not IMartenQueryable<>");
            }

            return martenQueryable;
        }

        #endregion Shared

        public static NpgsqlCommand ToCommand<T>(this IQueryable<T> queryable, FetchType fetchType = FetchType.FetchMany)
        {
            var q = queryable as MartenQueryable<T>;

            if (q == null)
            {
                throw new InvalidOperationException($"{nameof(ToCommand)} is only valid on Marten IQueryable objects");
            }

            return q.BuildCommand(fetchType);
        }

        public static IMartenQueryable<T> Include<T, TInclude>(this IQueryable<T> queryable, Expression<Func<T, object>> idSource,
            Action<TInclude> callback,
            JoinType joinType = JoinType.Inner)
        {
            return queryable.As<IMartenQueryable<T>>().Include(idSource, callback, joinType);
        }

        public static IMartenQueryable<T> Include<T, TInclude>(this IQueryable<T> queryable, Expression<Func<T, object>> idSource,
            IList<TInclude> list,
            JoinType joinType = JoinType.Inner)
        {
            return queryable.As<IMartenQueryable<T>>().Include(idSource, list, joinType);
        }

        public static IMartenQueryable<T> Include<T, TKey, TInclude>(this IQueryable<T> queryable, Expression<Func<T, object>> idSource,
            IDictionary<TKey, TInclude> dictionary,
            JoinType joinType = JoinType.Inner)
        {
            return queryable.As<IMartenQueryable<T>>().Include(idSource, dictionary, joinType);
        }
    }
}