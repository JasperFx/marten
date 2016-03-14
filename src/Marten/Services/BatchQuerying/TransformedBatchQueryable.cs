using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Marten.Services.BatchQuerying
{
    public class TransformedBatchQueryable<TDoc, TValue> : IBatchedFetcher<TValue>
    {
        private readonly BatchedQuery _parent;
        private readonly IQueryable<object> _inner;

        public TransformedBatchQueryable(BatchedQuery parent, IQueryable<object> inner)
        {
            _parent = parent;
            _inner = inner;
        }

        public Task<long> Count()
        {
            throw new NotSupportedException("Count() queries are not supported with Select() transforms in Batch queries");
        }

        public Task<long> Count(Expression<Func<TValue, bool>> filter)
        {
            throw new NotSupportedException("Count() queries are not supported with Select() transforms in Batch queries");
        }

        public Task<bool> Any()
        {
            throw new NotSupportedException("Any() queries are not supported with Select() transforms in Batch queries");
        }

        public Task<bool> Any(Expression<Func<TValue, bool>> filter)
        {
            throw new NotSupportedException("Any() queries are not supported with Select() transforms in Batch queries");
        }

        public Task<IList<TValue>> ToList()
        {
            throw new NotImplementedException();
        }

        public Task<TValue> First()
        {
            throw new NotImplementedException();
        }

        public Task<TValue> First(Expression<Func<TValue, bool>> filter)
        {
            throw new NotImplementedException();
        }

        public Task<TValue> FirstOrDefault()
        {
            throw new NotImplementedException();
        }

        public Task<TValue> FirstOrDefault(Expression<Func<TValue, bool>> filter)
        {
            throw new NotImplementedException();
        }

        public Task<TValue> Single()
        {
            throw new NotImplementedException();
        }

        public Task<TValue> Single(Expression<Func<TValue, bool>> filter)
        {
            throw new NotImplementedException();
        }

        public Task<TValue> SingleOrDefault()
        {
            throw new NotImplementedException();
        }

        public Task<TValue> SingleOrDefault(Expression<Func<TValue, bool>> filter)
        {
            throw new NotImplementedException();
        }
    }
}