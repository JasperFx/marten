using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq.Model;
using Marten.Schema;
using Marten.Services.Includes;

namespace Marten.Linq
{
    // TODO -- change the signature of this in 2.0 so that all the First/Single
    // methods are on IMartenQueryable<T>
    public interface IMartenQueryable
    {
        IEnumerable<IIncludeJoin> Includes { get; }

        IEnumerable<IWhereFragment> WhereFragments { get; }

        QueryStatistics Statistics { get; }

        Task<IReadOnlyList<TResult>> ToListAsync<TResult>(CancellationToken token);

        Task<bool> AnyAsync(CancellationToken token);

        Task<int> CountAsync(CancellationToken token);

        Task<long> CountLongAsync(CancellationToken token);

        Task<TResult> FirstAsync<TResult>(CancellationToken token);

        Task<TResult> FirstOrDefaultAsync<TResult>(CancellationToken token);

        Task<TResult> SingleAsync<TResult>(CancellationToken token);

        Task<TResult> SingleOrDefaultAsync<TResult>(CancellationToken token);

        Task<TResult> SumAsync<TResult>(CancellationToken token);

        Task<TResult> MinAsync<TResult>(CancellationToken token);

        Task<TResult> MaxAsync<TResult>(CancellationToken token);

        Task<double> AverageAsync(CancellationToken token);

        /// <param name="configureExplain">Configure EXPLAIN options as documented in <see href="https://www.postgresql.org/docs/9.6/static/sql-explain.html">EXPLAIN documentation</see></param>
        QueryPlan Explain(FetchType fetchType = FetchType.FetchMany, Action<IConfigureExplainExpressions> configureExplain = null);

        /// <summary>
        ///     Applies a pre-loaded Javascript transformation to the documents
        ///     returned by this query
        /// </summary>
        /// <typeparam name="TDoc"></typeparam>
        /// <param name="transformName"></param>
        /// <returns></returns>
        IQueryable<TDoc> TransformTo<TDoc>(string transformName);
    }

    public interface IMartenQueryable<T> : IQueryable<T>, IMartenQueryable
    {
        IMartenQueryable<T> Include<TInclude>(Expression<Func<T, object>> idSource, Action<TInclude> callback,
            JoinType joinType = JoinType.Inner);

        IMartenQueryable<T> Include<TInclude>(Expression<Func<T, object>> idSource, IList<TInclude> list,
            JoinType joinType = JoinType.Inner);

        IMartenQueryable<T> Include<TInclude, TKey>(Expression<Func<T, object>> idSource,
            IDictionary<TKey, TInclude> dictionary, JoinType joinType = JoinType.Inner);

        IMartenQueryable<T> AddWhereFragment(IWhereFragment whereFragment);

        IMartenQueryable<T> Stats(out QueryStatistics stats);

        LinqQuery<T> ToLinqQuery();

        /// <summary>
        /// Performs a full text search against <typeparamref name="TDoc"/>
        /// </summary>
        /// <param name="searchTerm">The text to search for.  May contain lexeme patterns used by PostgreSQL for full text searching</param>
        /// <param name="regConfig">The dictionary config passed to the 'to_tsquery' function, must match the config parameter used by <seealso cref="DocumentMapping.AddFullTextIndex(string)"/></param>
        /// <remarks>
        /// See: https://www.postgresql.org/docs/10/static/textsearch-controls.html#TEXTSEARCH-PARSING-QUERIES
        /// </remarks>
        IMartenQueryable<T> Search(string searchTerm, string regConfig = FullTextIndex.DefaultRegConfig);

        /// <summary>
        /// Performs a full text search against <typeparamref name="TDoc"/> using the 'plainto_tsquery' search function
        /// </summary>
        /// <param name="queryText">The text to search for.  May contain lexeme patterns used by PostgreSQL for full text searching</param>
        /// <param name="regConfig">The dictionary config passed to the 'to_tsquery' function, must match the config parameter used by <seealso cref="DocumentMapping.AddFullTextIndex(string)"/></param>
        /// <remarks>
        /// See: https://www.postgresql.org/docs/10/static/textsearch-controls.html#TEXTSEARCH-PARSING-QUERIES
        /// </remarks>
        IMartenQueryable<T> PlainTextSearch(string searchTerm, string regConfig = FullTextIndex.DefaultRegConfig);

        /// <summary>
        /// Performs a full text search against <typeparamref name="TDoc"/> using the 'phraseto_tsquery' search function
        /// </summary>
        /// <param name="queryText">The text to search for.  May contain lexeme patterns used by PostgreSQL for full text searching</param>
        /// <param name="regConfig">The dictionary config passed to the 'to_tsquery' function, must match the config parameter used by <seealso cref="DocumentMapping.AddFullTextIndex(string)"/></param>
        /// <remarks>
        /// See: https://www.postgresql.org/docs/10/static/textsearch-controls.html#TEXTSEARCH-PARSING-QUERIES
        /// </remarks>
        IMartenQueryable<T> PhraseSearch(string searchTerm, string regConfig = FullTextIndex.DefaultRegConfig);
    }
}