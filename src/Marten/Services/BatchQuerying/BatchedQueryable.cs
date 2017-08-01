using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Services.Includes;

namespace Marten.Services.BatchQuerying
{
    public class BatchedQueryable<T> : IBatchedQueryable<T> where T : class
    {
        private readonly BatchedQuery _parent;
        private IMartenQueryable<T> _inner;

        public BatchedQueryable(BatchedQuery parent, IMartenQueryable<T> inner)
        {
            _parent = parent;
            _inner = inner;
        }

        public IBatchedQueryable<T> Stats(out QueryStatistics stats)
        {
            _inner = _inner.Stats(out stats);
            return this;
        }

        public IBatchedQueryable<T> Where(Expression<Func<T, bool>> predicate)
        {
            _inner = _inner.Where(predicate).As<IMartenQueryable<T>>();
            return this;
        }

        public IBatchedQueryable<T> Skip(int count)
        {
            _inner = _inner.Skip(count).As<IMartenQueryable<T>>();
            return this;
        }

        public IBatchedQueryable<T> Take(int count)
        {
            _inner = _inner.Take(count).As<IMartenQueryable<T>>();
            return this;
        }

        public IBatchedQueryable<T> OrderBy<TKey>(Expression<Func<T, TKey>> expression)
        {
            _inner = _inner.OrderBy(expression).As<IMartenQueryable<T>>();
            return this;
        }


        public IBatchedQueryable<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> expression)
        {
            _inner = _inner.OrderByDescending(expression).As<IMartenQueryable<T>>();
            return this;
        }

        public ITransformedBatchQueryable<TValue> Select<TValue>(Expression<Func<T, TValue>> selection)
        {
            return new TransformedBatchQueryable<TValue>(_parent, _inner.Select(selection).As<IMartenQueryable<TValue>>());
        }

       

        public IBatchedQueryable<T> Include<TInclude>(Expression<Func<T, object>> idSource, Action<TInclude> callback, JoinType joinType = JoinType.Inner) where TInclude : class
        {
            _inner = _inner.Include(idSource, callback, joinType);
            return this;
        }

        public IBatchedQueryable<T> Include<TInclude>(Expression<Func<T, object>> idSource, IList<TInclude> list, JoinType joinType = JoinType.Inner) where TInclude : class
        {
            _inner = _inner.Include(idSource, list, joinType);
            return this;
        }

        public IBatchedQueryable<T> Include<TInclude, TKey>(Expression<Func<T, object>> idSource, IDictionary<TKey, TInclude> dictionary,
            JoinType joinType = JoinType.Inner) where TInclude : class
        {
            _inner = _inner.Include(idSource, dictionary, joinType);
            return this;
        }

        public Task<long> Count()
        {
            return _parent.Count(_inner);
        }

        public Task<long> Count(Expression<Func<T, bool>> filter)
        {
            var queryable = _inner.Where(filter).As<IMartenQueryable<T>>();
            return _parent.Count(queryable);
        }

        public Task<bool> Any()
        {
            return _parent.Any(_inner);
        }

        public Task<bool> Any(Expression<Func<T, bool>> filter)
        {
            return _parent.Any(_inner.Where(filter).As<IMartenQueryable<T>>());
        }

        public Task<IReadOnlyList<T>> ToList()
        {
            return _parent.Query(_inner);
        }

        public Task<T> First()
        {
            return _parent.First<T>(_inner);
        }

        public Task<T> First(Expression<Func<T, bool>> filter)
        {
            return _parent.First<T>(_inner.Where(filter).As<IMartenQueryable<T>>());
        }

        public Task<T> FirstOrDefault()
        {
            return _parent.FirstOrDefault<T>(_inner);
        }

        public Task<T> FirstOrDefault(Expression<Func<T, bool>> filter)
        {
            return _parent.FirstOrDefault<T>(_inner.Where(filter).As<IMartenQueryable<T>>());
        }

        public Task<T> Single()
        {
            return _parent.Single<T>(_inner);
        }

        public Task<T> Single(Expression<Func<T, bool>> filter)
        {
            return _parent.Single<T>(_inner.Where(filter).As<IMartenQueryable<T>>());
        }

        public Task<T> SingleOrDefault()
        {
            return _parent.SingleOrDefault<T>(_inner);
        }

        public Task<T> SingleOrDefault(Expression<Func<T, bool>> filter)
        {
            return _parent.SingleOrDefault<T>(_inner.Where(filter).As<IMartenQueryable<T>>());
        }

        public Task<TResult> Min<TResult>(Expression<Func<T, TResult>> expression)
        {
            return _parent.Min(_inner.Select(expression));
        }

        public Task<TResult> Max<TResult>(Expression<Func<T, TResult>> expression)
        {
            return _parent.Max(_inner.Select(expression));
        }

        public Task<TResult> Sum<TResult>(Expression<Func<T, TResult>> expression)
        {
            return _parent.Sum(_inner.Select(expression));
        }

        public Task<double> Average(Expression<Func<T, object>> expression)
        {
            return _parent.Average(_inner.Select(expression));
        }
    }
}