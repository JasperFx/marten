using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Services.Includes;
using Remotion.Linq;

namespace Marten.Linq
{

    public interface IMartenQueryable
    {
        Task<IList<TResult>> ToListAsync<TResult>(CancellationToken token);
        Task<bool> AnyAsync(CancellationToken token);
        Task<int> CountAsync(CancellationToken token);
        Task<long> CountLongAsync(CancellationToken token);
        Task<TResult> FirstAsync<TResult>(CancellationToken token);
        Task<TResult> FirstOrDefaultAsync<TResult>(CancellationToken token);
        Task<TResult> SingleAsync<TResult>(CancellationToken token);
        Task<TResult> SingleOrDefaultAsync<TResult>(CancellationToken token);
        IEnumerable<IIncludeJoin> Includes { get; }

        QueryStatistics Statistics { get; }

        Task<TResult> SumAsync<TResult>(CancellationToken token);
        Task<TResult> MinAsync<TResult>(CancellationToken token);
        Task<TResult> MaxAsync<TResult>(CancellationToken token);
        Task<double> AverageAsync(CancellationToken token);
        QueryPlan Explain(FetchType fetchType = FetchType.FetchMany);
    }


    public interface IMartenQueryable<T> : IQueryable<T>, IMartenQueryable
    {
        IMartenQueryable<T> Include<TInclude>(Expression<Func<T, object>> idSource, Action<TInclude> callback, JoinType joinType = JoinType.Inner) where TInclude : class;
        IMartenQueryable<T> Include<TInclude>(Expression<Func<T, object>> idSource, IList<TInclude> list, JoinType joinType = JoinType.Inner) where TInclude : class;
        IMartenQueryable<T> Include<TInclude, TKey>(Expression<Func<T, object>> idSource, IDictionary<TKey, TInclude> dictionary, JoinType joinType = JoinType.Inner) where TInclude : class;


        IMartenQueryable<T> Stats(out QueryStatistics stats);
    }

    public class QueryStatistics
    {
        public long TotalResults { get; set; }
    }
}