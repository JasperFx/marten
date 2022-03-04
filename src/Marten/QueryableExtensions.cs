using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Npgsql;
#nullable enable
namespace Marten
{
    public static class QueryableExtensions
    {

        /// <summary>
        /// Fetch the Postgresql QueryPlan for the Linq query
        /// </summary>
        /// <param name="queryable"></param>
        /// <param name="configureExplain"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static QueryPlan Explain<T>(this IQueryable<T> queryable, Action<IConfigureExplainExpressions>? configureExplain = null)
        {
            return queryable.As<IMartenQueryable<T>>().Explain(configureExplain: configureExplain);
        }

        #region ToList

        /// <summary>
        /// Fetch results asynchronously to a read only list
        /// </summary>
        /// <param name="queryable"></param>
        /// <param name="token"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static Task<IReadOnlyList<T>> ToListAsync<T>(this IQueryable<T> queryable,
            CancellationToken token = default)
        {
            return queryable.As<IMartenQueryable>().ToListAsync<T>(token);
        }

        #endregion ToList

        #region Any


        public static Task<bool> AnyAsync<TSource>(
            this IQueryable<TSource> source,
            CancellationToken token = default)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            return source.As<IMartenQueryable>().AnyAsync(token);
        }

        public static Task<bool> AnyAsync<TSource>(
            this IQueryable<TSource> source,
            Expression<Func<TSource, bool>> predicate,
            CancellationToken token = default)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));

            return source.Where(predicate).AnyAsync(token);
        }

        #endregion Any

        #region Aggregate Functions

        public static Task<TResult> SumAsync<TSource, TResult>(
            this IQueryable<TSource> source, Expression<Func<TSource, TResult>> expression,
            CancellationToken token = default)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            return source.Select(expression).As<IMartenQueryable>().SumAsync<TResult>(token);
        }

        public static Task<TResult> MaxAsync<TSource, TResult>(
            this IQueryable<TSource> source, Expression<Func<TSource, TResult>> expression,
            CancellationToken token = default)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            return source.Select(expression).As<IMartenQueryable>().MaxAsync<TResult>(token);
        }

        public static Task<TResult> MinAsync<TSource, TResult>(
            this IQueryable<TSource> source, Expression<Func<TSource, TResult>> expression,
            CancellationToken token = default)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            return source.Select(expression).As<IMartenQueryable>().MinAsync<TResult>(token);
        }

        public static Task<double> AverageAsync<TSource, TMember>(
            this IQueryable<TSource> source, Expression<Func<TSource, TMember>> expression,
            CancellationToken token = default)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            return source.Select(expression).As<IMartenQueryable>().AverageAsync(token);
        }

        #endregion Aggregate Functions

        #region Count/LongCount/Sum

        public static Task<int> CountAsync<TSource>(
            this IQueryable<TSource> source,
            CancellationToken token = default)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            return source.As<IMartenQueryable>().CountAsync(token);
        }

        public static Task<int> CountAsync<TSource>(
            this IQueryable<TSource> source,
            Expression<Func<TSource, bool>> predicate,
            CancellationToken token = default)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));

            return source.Where(predicate).CountAsync(token);
        }

        public static Task<long> LongCountAsync<TSource>(
            this IQueryable<TSource> source,
            CancellationToken token = default)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            return source.As<IMartenQueryable>().CountLongAsync(token);
        }

        public static Task<long> LongCountAsync<TSource>(
            this IQueryable<TSource> source,
            Expression<Func<TSource, bool>> predicate,
            CancellationToken token = default)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));

            return source.Where(predicate).LongCountAsync(token);
        }

        #endregion Count/LongCount/Sum

        #region First/FirstOrDefault

        public static Task<TSource> FirstAsync<TSource>(
            this IQueryable<TSource> source,
            CancellationToken token = default)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            return source.As<IMartenQueryable>().FirstAsync<TSource>(token);
        }

        public static Task<TSource> FirstAsync<TSource>(
            this IQueryable<TSource> source,
            Expression<Func<TSource, bool>> predicate,
            CancellationToken token = default)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));

            return source.Where(predicate).FirstAsync(token);
        }

        public static Task<TSource?> FirstOrDefaultAsync<TSource>(
            this IQueryable<TSource> source,
            CancellationToken token = default)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            return source.As<IMartenQueryable>().FirstOrDefaultAsync<TSource>(token);
        }

        public static Task<TSource?> FirstOrDefaultAsync<TSource>(
            this IQueryable<TSource> source,
            Expression<Func<TSource, bool>> predicate,
            CancellationToken token = default)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));

            return source.Where(predicate).FirstOrDefaultAsync(token);
        }

        #endregion First/FirstOrDefault

        #region Single/SingleOrDefault

        public static Task<TSource> SingleAsync<TSource>(
            this IQueryable<TSource> source,
            CancellationToken token = default)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            return source.As<IMartenQueryable>().SingleAsync<TSource>(token);
        }

        public static Task<TSource> SingleAsync<TSource>(
            this IQueryable<TSource> source,
            Expression<Func<TSource, bool>> predicate,
            CancellationToken token = default)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));

            return source.Where(predicate).SingleAsync(token);
        }

        public static Task<TSource?> SingleOrDefaultAsync<TSource>(
            this IQueryable<TSource> source,
            CancellationToken token = default)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            return source.As<IMartenQueryable>().SingleOrDefaultAsync<TSource>(token);
        }

        public static Task<TSource?> SingleOrDefaultAsync<TSource>(
            this IQueryable<TSource> source,
            Expression<Func<TSource, bool>> predicate,
            CancellationToken token = default)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));

            return source.Where(predicate).SingleOrDefaultAsync(token);
        }

        #endregion Single/SingleOrDefault

        /// <summary>
        /// Builds the database command that would be used to execute this Linq query
        /// </summary>
        /// <param name="queryable"></param>
        /// <param name="fetchType"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public static NpgsqlCommand ToCommand<T>(this IQueryable<T> queryable, FetchType fetchType = FetchType.FetchMany)
        {
            if (queryable is MartenLinqQueryable<T> q1)
            {
                return q1.ToPreviewCommand(fetchType);
            }

            throw new InvalidOperationException($"{nameof(ToCommand)} is only valid on Marten IQueryable objects");
        }

        /// <summary>
        /// Fetch a related document of type TInclude when executing the Linq query and
        /// call the supplied callback for each result
        /// </summary>
        /// <param name="queryable"></param>
        /// <param name="idSource"></param>
        /// <param name="callback"></param>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="TInclude"></typeparam>
        /// <returns></returns>
        public static IMartenQueryable<T> Include<T, TInclude>(this IQueryable<T> queryable,
            Expression<Func<T, object>> idSource,
            Action<TInclude> callback) where TInclude : notnull
        {
            return queryable.As<MartenLinqQueryable<T>>().Include(idSource, callback);
        }

        /// <summary>
        /// Fetch a related document of type TInclude when executing the Linq query and
        /// store these documents in the supplied List<TInclude>
        /// </summary>
        /// <param name="queryable"></param>
        /// <param name="idSource"></param>
        /// <param name="list"></param>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="TInclude"></typeparam>
        /// <returns></returns>
        public static IMartenQueryable<T> Include<T, TInclude>(this IQueryable<T> queryable,
            Expression<Func<T, object>> idSource,
            IList<TInclude> list) where TInclude : notnull
        {
            return queryable.As<MartenLinqQueryable<T>>().Include(idSource, list);
        }

        /// <summary>
        /// Fetch related documents when executing the Linq query and store the related documents
        /// into the supplied dictionary
        /// </summary>
        /// <param name="queryable"></param>
        /// <param name="idSource"></param>
        /// <param name="dictionary"></param>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TInclude"></typeparam>
        /// <returns></returns>
        public static IMartenQueryable<T> Include<T, TKey, TInclude>(this IQueryable<T> queryable,
            Expression<Func<T, object>> idSource,
            IDictionary<TKey, TInclude> dictionary) where TInclude : notnull where TKey : notnull
        {
            return queryable.As<MartenLinqQueryable<T>>()
                .Include(idSource, dictionary);
        }

        /// <summary>
        /// Execute this query to an IAsyncEnumerable. This is valuable for reading
        /// and processing large result sets without having to keep the entire
        /// result set in memory
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public static IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IQueryable<T> queryable,
            CancellationToken token = default)
        {
            return queryable.As<IMartenQueryable<T>>().ToAsyncEnumerable(token);
        }


        /// <summary>
        /// Write the raw persisted JSON for the Linq query directly to the destination stream
        /// </summary>
        /// <param name="destination"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public static Task<int> StreamJsonArray<T>(this IQueryable<T> queryable, Stream destination, CancellationToken token = default)
        {
            return queryable.As<MartenLinqQueryable<T>>().StreamJsonArray(destination, token);
        }

        public static Task<string> ToJsonArray<T>(this IQueryable<T> queryable, CancellationToken token = default)
        {
            return queryable.As<MartenLinqQueryable<T>>().ToJsonArray(token);
        }

        public static Task StreamJsonFirst<T>(this IQueryable<T> queryable, Stream destination, CancellationToken token = default)
        {
            return queryable.As<MartenLinqQueryable<T>>().StreamJsonFirst(destination, token);
        }

        public static async Task<bool> StreamJsonFirstOrDefault<T>(this IQueryable<T> queryable, Stream destination, CancellationToken token = default)
        {
            return (await queryable.As<MartenLinqQueryable<T>>().StreamJsonFirstOrDefault(destination, token).ConfigureAwait(false)) > 0;
        }

        public static Task StreamJsonSingle<T>(this IQueryable<T> queryable, Stream destination, CancellationToken token = default)
        {
            return queryable.As<MartenLinqQueryable<T>>().StreamJsonFirst(destination, token);
        }

        public static Task StreamJsonSingleOrDefault<T>(this IQueryable<T> queryable, Stream destination, CancellationToken token = default)
        {
            return queryable.As<MartenLinqQueryable<T>>().StreamJsonSingleOrDefault(destination, token);
        }

        public static Task<string> ToJsonFirst<T>(this IQueryable<T> queryable, CancellationToken token = default)
        {
            return queryable.As<MartenLinqQueryable<T>>().ToJsonFirst(token);
        }

        public static Task<string?> ToJsonFirstOrDefault<T>(this IQueryable<T> queryable, CancellationToken token = default)
        {
            return queryable.As<MartenLinqQueryable<T>>().ToJsonFirstOrDefault(token);
        }

        public static Task<string> ToJsonSingle<T>(this IQueryable<T> queryable, CancellationToken token = default)
        {
            return queryable.As<MartenLinqQueryable<T>>().ToJsonSingle(token);
        }

        public static Task<string?> ToJsonSingleOrDefault<T>(this IQueryable<T> queryable, CancellationToken token = default)
        {
            return queryable.As<MartenLinqQueryable<T>>().ToJsonSingleOrDefault(token);
        }

        #region OrderBy
        /// <summary>
        /// Order by multiple properties in ascending order i.e. "prop1", "prop2"
        /// or order by multiple properties with their respective sort order i.e. "prop1", "prop2 ASC|asc", "prop3 DESC|desc"
        /// </summary>
        /// <param name="queryable"></param>
        /// <param name="properties"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static IOrderedQueryable<T> OrderBy<T>(this IQueryable<T> queryable, params string[] properties)
        {
            if (properties.Length == 0)
                throw new ArgumentException($"{nameof(properties)} should at least have one property",
                    nameof(properties));

            // handle the first order by property
            var orderedQueryable = queryable.OrderBy(properties.First());

            // handle the rest of the properties
            return properties.Skip(1).Aggregate(orderedQueryable, (current, prop) => current.OrderBy(prop));
        }

        /// <summary>
        /// Order by a single property in ascending order
        /// </summary>
        /// <param name="queryable"></param>
        /// <param name="property"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static IOrderedQueryable<T> OrderBy<T>(this IQueryable<T> queryable, string property)
        {
            var propParts = property.Split(' ').Take(2).ToArray();

            string propertyName;
            string sortOrder;
            if (propParts.Length == 2)
            {
                propertyName = propParts[0];
                sortOrder = propParts[1].ToLower();
            }
            else
            {
                propertyName = propParts[0];
                sortOrder = "asc";
            }

            return sortOrder == "desc"
                ? ApplyOrder<T>(queryable, propertyName, "OrderByDescending")
                : ApplyOrder<T>(queryable, propertyName, "OrderBy");
        }

        /// <summary>
        /// Order by a single property in descending order
        /// </summary>
        /// <param name="queryable"></param>
        /// <param name="property"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static IOrderedQueryable<T> OrderByDescending<T>(this IQueryable<T> queryable, string property)
        {
            return ApplyOrder<T>(queryable, property, "OrderByDescending");
        }

        /// <summary>
        /// Chain another order by using a single property in ascending order
        /// </summary>
        /// <param name="queryable"></param>
        /// <param name="property"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static IOrderedQueryable<T> ThenBy<T>(this IOrderedQueryable<T> queryable, string property)
        {
            return ApplyOrder<T>(queryable, property, "ThenBy");
        }

        /// <summary>
        /// Chain another order by using a single property in descending order
        /// </summary>
        /// <param name="queryable"></param>
        /// <param name="property"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static IOrderedQueryable<T> ThenByDescending<T>(this IOrderedQueryable<T> queryable, string property)
        {
            return ApplyOrder<T>(queryable, property, "ThenByDescending");
        }

        private static IOrderedQueryable<T> ApplyOrder<T>(
            IQueryable<T> queryable,
            string property,
            string methodName)
        {
            var props = property.Split('.');
            var type = typeof(T);
            var arg = Expression.Parameter(type, "x");
            Expression expr = arg;
            foreach (var prop in props)
            {
                var pi = type.GetProperty(prop);

                if (pi == null)
                {
                    throw new ArgumentException($"Order by property {prop} not found in type {typeof(T).FullName}", nameof(property));
                }

                expr = Expression.Property(expr, pi);
                type = pi.PropertyType;
            }

            var delegateType = typeof(Func<,>).MakeGenericType(typeof(T), type);
            var lambda = Expression.Lambda(delegateType, expr, arg);

            var result = typeof(Queryable).GetMethods().Single(
                    method => method.Name == methodName
                              && method.IsGenericMethodDefinition
                              && method.GetGenericArguments().Length == 2
                              && method.GetParameters().Length == 2)
                .MakeGenericMethod(typeof(T), type)
                .Invoke(null, new object[] {queryable, lambda});
            return (IOrderedQueryable<T>)result;
        }
        #endregion
    }
}
