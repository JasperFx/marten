using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten.Schema;

namespace Marten.Services.BatchQuerying
{
    public interface IBatchedQueryable<T>
    {
        IBatchedQueryable<T> Where(Expression<Func<T, bool>> predicate);
        IBatchedQueryable<T> Skip(int count);
        IBatchedQueryable<T> Take(int count);
        IBatchedQueryable<T> OrderBy<TKey>(Expression<Func<T, TKey>> expression);
        IBatchedQueryable<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> expression);

        /// <summary>
        /// Return a count of all the documents of type "T"
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task<long> Count();

        /// <summary>
        /// Return a count of all the documents of type "T" that match the query
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task<long> Count(Expression<Func<T, bool>> filter);

        /// <summary>
        /// Query for the existence of any documents of type "T" matching the query
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task<bool> Any();

        /// <summary>
        /// Query for the existence of any documents of type "T"
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task<bool> Any(Expression<Func<T, bool>> filter);
        Task<IList<T>> ToList();
        Task<T> First();

        /// <summary>
        /// Find the first document of type "T" matching this query. Will throw an exception if there are no matching documents
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task<T> First(Expression<Func<T, bool>> filter);

        /// <summary>
        /// Find the first document of type "T" that matches the query. Will return null if no documents match.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task<T> FirstOrDefault();
        Task<T> FirstOrDefault(Expression<Func<T, bool>> filter);

        /// <summary>
        /// Returns the single document of type "T" matching this query. Will 
        /// throw an exception if the results are null or contain more than one
        /// document
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task<T> Single();

        /// <summary>
        /// Returns the single document of type "T" matching this query. Will 
        /// throw an exception if the results are null or contain more than one
        /// document
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task<T> Single(Expression<Func<T, bool>> filter);

        /// <summary>
        /// Returns the single document of type "T" matching this query or null. Will 
        /// throw an exception if the results contain more than one
        /// document
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task<T> SingleOrDefault();

        /// <summary>
        /// Returns the single document of type "T" matching this query or null. Will 
        /// throw an exception if the results contain more than one
        /// document
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task<T> SingleOrDefault(Expression<Func<T, bool>> filter);
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