using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Linq.Model;
using Marten.Services.Includes;
using Marten.V4Internals.Sessions;
using Remotion.Linq;
using Remotion.Linq.Clauses.ResultOperators;

namespace Marten.V4Internals.Linq
{
    public class V4Queryable<T> : QueryableBase<T>, IMartenQueryable<T>
    {
        private readonly IMartenSession _session;

        public V4Queryable(MartenSessionBase session) : base(session)
        {
            _session = session;
        }

        public V4Queryable(MartenSessionBase session, Expression expression) : base(session, expression)
        {
            _session = session;
        }



        public IEnumerable<IIncludeJoin> Includes { get; }
        public QueryStatistics Statistics { get; }

        public Task<IReadOnlyList<TResult>> ToListAsync<TResult>(CancellationToken token)
        {
            return _session.ExecuteAsync<IReadOnlyList<TResult>>(Expression, token);
        }

        public Task<bool> AnyAsync(CancellationToken token)
        {
            // TODO -- flyweight for the operator
            return _session.ExecuteAsync<bool>(Expression, token, new AnyResultOperator());
        }

        public Task<int> CountAsync(CancellationToken token)
        {
            // TODO -- flyweight for the operator
            return _session.ExecuteAsync<int>(Expression, token, new CountResultOperator());
        }

        public Task<long> CountLongAsync(CancellationToken token)
        {
            // TODO -- flyweight for the operator
            return _session.ExecuteAsync<long>(Expression, token, new LongCountResultOperator());
        }

        public Task<TResult> FirstAsync<TResult>(CancellationToken token)
        {
            // TODO -- flyweight for the operator
            return _session.ExecuteAsync<TResult>(Expression, token, new FirstResultOperator(false));
        }

        public Task<TResult> FirstOrDefaultAsync<TResult>(CancellationToken token)
        {
            // TODO -- flyweight for the operator
            return _session.ExecuteAsync<TResult>(Expression, token, new FirstResultOperator(true));
        }

        public Task<TResult> SingleAsync<TResult>(CancellationToken token)
        {
            // TODO -- flyweight for the operator
            return _session.ExecuteAsync<TResult>(Expression, token, new SingleResultOperator(false));
        }

        public Task<TResult> SingleOrDefaultAsync<TResult>(CancellationToken token)
        {
            // TODO -- flyweight for the operator
            return _session.ExecuteAsync<TResult>(Expression, token, new SingleResultOperator(true));
        }

        public Task<TResult> SumAsync<TResult>(CancellationToken token)
        {
            // TODO -- flyweight for the operator
            return _session.ExecuteAsync<TResult>(Expression, token, new SumResultOperator());
        }

        public Task<TResult> MinAsync<TResult>(CancellationToken token)
        {
            // TODO -- flyweight for the operator
            return _session.ExecuteAsync<TResult>(Expression, token, new MinResultOperator());
        }

        public Task<TResult> MaxAsync<TResult>(CancellationToken token)
        {
            // TODO -- flyweight for the operator
            return _session.ExecuteAsync<TResult>(Expression, token, new MaxResultOperator());
        }

        public Task<double> AverageAsync(CancellationToken token)
        {
            // TODO -- flyweight for the operator
            return _session.ExecuteAsync<double>(Expression, token, new AverageResultOperator());
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
