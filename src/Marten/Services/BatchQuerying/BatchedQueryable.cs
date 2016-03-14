using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten.Schema;

namespace Marten.Services.BatchQuerying
{
    public interface IBatchedQueryable<T> : IBatchedFetcher<T>
    {
        IBatchedQueryable<T> Where(Expression<Func<T, bool>> predicate);
        IBatchedQueryable<T> Skip(int count);
        IBatchedQueryable<T> Take(int count);
        IBatchedQueryable<T> OrderBy<TKey>(Expression<Func<T, TKey>> expression);
        IBatchedQueryable<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> expression);

        IBatchedFetcher<TValue> Select<TValue>(Expression<Func<T, TValue>> selection);


    }

    public class BatchedQueryable<T> : IBatchedQueryable<T> where T : class
    {
        private readonly BatchedQuery _parent;
        private IQueryable<T> _inner;

        public BatchedQueryable(BatchedQuery parent, IQueryable<T> inner)
        {
            _parent = parent;
            _inner = inner;
        }

        public IBatchedQueryable<T> Where(Expression<Func<T, bool>> predicate)
        {
            _inner = _inner.Where(predicate);
            return this;
        }

        public IBatchedQueryable<T> Skip(int count)
        {
            _inner = _inner.Skip(count);
            return this;
        }

        public IBatchedQueryable<T> Take(int count)
        {
            _inner = _inner.Take(count);
            return this;
        }

        public IBatchedQueryable<T> OrderBy<TKey>(Expression<Func<T, TKey>> expression)
        {
            _inner = _inner.OrderBy(expression);
            return this;
        }


        public IBatchedQueryable<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> expression)
        {
            _inner = _inner.OrderByDescending(expression);
            return this;
        }

        public IBatchedFetcher<TValue> Select<TValue>(Expression<Func<T, TValue>> selection)
        {
            _inner.Select(selection);
            return new TransformedBatchQueryable<T,TValue>(_parent, _inner);
            
        }

        public Task<long> Count()
        {
            return _parent.AddHandler<T, CountHandler, long>(q => _inner);
        }

        public Task<long> Count(Expression<Func<T, bool>> filter)
        {
            return _parent.AddHandler<T, CountHandler, long>(q => _inner.Where(filter));
        }

        public Task<bool> Any()
        {
            return _parent.AddHandler<T, AnyHandler, bool>(q => _inner);
        }

        public Task<bool> Any(Expression<Func<T, bool>> filter)
        {
            return _parent.AddHandler<T, AnyHandler, bool>(q => _inner.Where(filter));
        }

        public Task<IList<T>> ToList()
        {
            return _parent.Query<T>(q => _inner);
        }

        public Task<T> First()
        {
            return _parent.First<T>(q => _inner);
        }

        public Task<T> First(Expression<Func<T, bool>> filter)
        {
            return _parent.First<T>(q => _inner.Where(filter));
        }

        public Task<T> FirstOrDefault()
        {
            return _parent.FirstOrDefault<T>(q => _inner);
        }

        public Task<T> FirstOrDefault(Expression<Func<T, bool>> filter)
        {
            return _parent.FirstOrDefault<T>(q => _inner.Where(filter));
        }

        public Task<T> Single()
        {
            return _parent.Single<T>(q => _inner);
        }

        public Task<T> Single(Expression<Func<T, bool>> filter)
        {
            return _parent.Single<T>(q => _inner.Where(filter));
        }

        public Task<T> SingleOrDefault()
        {
            return _parent.SingleOrDefault<T>(q => _inner);
        }

        public Task<T> SingleOrDefault(Expression<Func<T, bool>> filter)
        {
            return _parent.SingleOrDefault<T>(q => _inner.Where(filter));
        }
    }
}