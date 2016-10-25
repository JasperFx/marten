using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq.Model;
using Marten.Services.Includes;

namespace Marten.Linq
{
    // TODO -- change the signature of this in 2.0 so that all the First/Single
    // methods are on IMartenQueryable<T>
    public interface IMartenQueryable
    {
        IEnumerable<IIncludeJoin> Includes { get; }

        QueryStatistics Statistics { get; }
        Task<IList<TResult>> ToListAsync<TResult>(CancellationToken token);
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
        QueryPlan Explain(FetchType fetchType = FetchType.FetchMany);

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


        IMartenQueryable<T> Stats(out QueryStatistics stats);

        LinqQuery<T> ToLinqQuery();
    }

    public class QueryStatistics
    {
        public long TotalResults { get; set; }
    }
}