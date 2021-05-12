using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

#nullable enable
namespace Marten.Linq
{
    public interface IMartenQueryable
    {
        QueryStatistics Statistics { get; }

        Task<IReadOnlyList<TResult>> ToListAsync<TResult>(CancellationToken token);

        Task<bool> AnyAsync(CancellationToken token);

        Task<int> CountAsync(CancellationToken token);

        Task<long> CountLongAsync(CancellationToken token);

        Task<TResult> FirstAsync<TResult>(CancellationToken token);

        Task<TResult?> FirstOrDefaultAsync<TResult>(CancellationToken token);

        Task<TResult> SingleAsync<TResult>(CancellationToken token);

        Task<TResult?> SingleOrDefaultAsync<TResult>(CancellationToken token);

        Task<TResult> SumAsync<TResult>(CancellationToken token);

        Task<TResult> MinAsync<TResult>(CancellationToken token);

        Task<TResult> MaxAsync<TResult>(CancellationToken token);

        Task<double> AverageAsync(CancellationToken token);

        /// <param name="configureExplain">Configure EXPLAIN options as documented in <see href="https://www.postgresql.org/docs/9.6/static/sql-explain.html">EXPLAIN documentation</see></param>
        QueryPlan Explain(FetchType fetchType = FetchType.FetchMany, Action<IConfigureExplainExpressions>? configureExplain = null);

        /// <summary>
        ///     Applies a pre-loaded Javascript transformation to the documents
        ///     returned by this query
        /// </summary>
        /// <typeparam name="TDoc"></typeparam>
        /// <param name="transformName"></param>
        /// <returns></returns>
        IQueryable<TDoc> TransformTo<TDoc>(string transformName);

        /// <summary>
        /// Retrieve the document data as a JSON array string
        /// </summary>
        /// <returns></returns>
        string ToJsonArray();

        /// <summary>
        /// Retrieve the document data as a JSON array string
        /// </summary>
        /// <returns></returns>
        Task<string> ToJsonArrayAsync(CancellationToken token);

        /// <summary>
        /// Retrieve the document data as a JSON array string
        /// </summary>
        /// <returns></returns>
        Task<string> ToJsonArrayAsync();
    }

    public interface IMartenQueryable<T>: IQueryable<T>, IMartenQueryable
    {
        /// <summary>
        /// Also fetch related documents, and call the callback lambda for each
        /// related document
        /// </summary>
        /// <param name="idSource"></param>
        /// <param name="callback"></param>
        /// <typeparam name="TInclude"></typeparam>
        /// <returns></returns>
        IMartenQueryable<T> Include<TInclude>(Expression<Func<T, object>> idSource, Action<TInclude> callback) where TInclude : notnull;

        /// <summary>
        /// Also fetch related documents, and add the related documents to
        /// the supplied list
        /// </summary>
        /// <param name="idSource"></param>
        /// <param name="list"></param>
        /// <typeparam name="TInclude"></typeparam>
        /// <returns></returns>
        IMartenQueryable<T> Include<TInclude>(Expression<Func<T, object>> idSource, IList<TInclude> list) where TInclude : notnull;

        /// <summary>
        /// Also fetch related documents, and add the related documents to
        /// the supplied dictionary organized by the identity of the related document
        /// </summary>
        /// <param name="idSource"></param>
        /// <param name="dictionary"></param>
        /// <typeparam name="TInclude"></typeparam>
        /// <typeparam name="TKey"></typeparam>
        /// <returns></returns>
        IMartenQueryable<T> Include<TInclude, TKey>(Expression<Func<T, object>> idSource,
            IDictionary<TKey, TInclude> dictionary) where TInclude : notnull where TKey : notnull;

        /// <summary>
        /// Retrieve the total number of persisted rows in the database that match this
        /// query. Useful for server side paging.
        /// </summary>
        /// <param name="stats"></param>
        /// <returns></returns>
        IMartenQueryable<T> Stats(out QueryStatistics stats);

        /// <summary>
        /// Execute this query to an IAsyncEnumerable. This is valuable for reading
        /// and processing large result sets without having to keep the entire
        /// result set in memory
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        IAsyncEnumerable<T> ToAsyncEnumerable(CancellationToken token = default);


        /// <summary>
        /// Write the raw persisted JSON for the Linq query directly to the destination stream
        /// </summary>
        /// <param name="destination"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task StreamManyAsync(Stream destination, CancellationToken token);

        /// <summary>
        /// Write the raw persisted JSON directly to the destination stream. Uses "FirstOrDefault()"
        /// rules. Returns true if there is at least one record.
        /// </summary>
        /// <param name="destination"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task<bool> StreamOne(Stream destination, CancellationToken token);

    }
}
