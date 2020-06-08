using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Linq.Model;
using Marten.Services.Includes;
using Remotion.Linq;

namespace Marten.V4Internals.Linq
{
    public class V4Queryable<T> : QueryableBase<T>, IMartenQueryable<T>
    {

        public V4Queryable(IMartenSession session) : base(session)
        {
        }

        public V4Queryable(IMartenSession session, Expression expression) : base(session, expression)
        {
        }



        public IEnumerable<IIncludeJoin> Includes { get; }
        public QueryStatistics Statistics { get; }
        public Task<IReadOnlyList<TResult>> ToListAsync<TResult>(CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public Task<bool> AnyAsync(CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public Task<int> CountAsync(CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public Task<long> CountLongAsync(CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public Task<TResult> FirstAsync<TResult>(CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public Task<TResult> FirstOrDefaultAsync<TResult>(CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public Task<TResult> SingleAsync<TResult>(CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public Task<TResult> SingleOrDefaultAsync<TResult>(CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public Task<TResult> SumAsync<TResult>(CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public Task<TResult> MinAsync<TResult>(CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public Task<TResult> MaxAsync<TResult>(CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public Task<double> AverageAsync(CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public QueryPlan Explain(FetchType fetchType = FetchType.FetchMany, Action<IConfigureExplainExpressions> configureExplain = null)
        {
            throw new NotImplementedException();
        }

        public IQueryable<TDoc> TransformTo<TDoc>(string transformName)
        {
            throw new NotImplementedException();
        }

        public IMartenQueryable<T> Include<TInclude>(Expression<Func<T, object>> idSource, Action<TInclude> callback, JoinType joinType = JoinType.Inner)
        {
            throw new NotImplementedException();
        }

        public IMartenQueryable<T> Include<TInclude>(Expression<Func<T, object>> idSource, IList<TInclude> list, JoinType joinType = JoinType.Inner)
        {
            throw new NotImplementedException();
        }

        public IMartenQueryable<T> Include<TInclude, TKey>(Expression<Func<T, object>> idSource, IDictionary<TKey, TInclude> dictionary,
            JoinType joinType = JoinType.Inner)
        {
            throw new NotImplementedException();
        }

        public IMartenQueryable<T> Stats(out QueryStatistics stats)
        {
            throw new NotImplementedException();
        }

        // TODO -- try to get rid of this
        public LinqQuery<T> ToLinqQuery()
        {
            throw new NotImplementedException();
        }
    }
}
