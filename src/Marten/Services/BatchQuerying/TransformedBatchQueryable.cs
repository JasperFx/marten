using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Marten.Services.BatchQuerying
{
    public interface ITransformedBatchQueryable<TValue>
    {
        Task<IList<TValue>> ToList();
        Task<TValue> First();
        Task<TValue> FirstOrDefault();
        Task<TValue> Single();
        Task<TValue> SingleOrDefault();
    }

    public class TransformedBatchQueryable<TValue> : ITransformedBatchQueryable<TValue>
    {
        private readonly BatchedQuery _parent;
        private readonly IQueryable<TValue> _inner;

        public TransformedBatchQueryable(BatchedQuery parent, IQueryable<TValue> inner)
        {
            _parent = parent;
            _inner = inner;
        }

        public Task<IList<TValue>> ToList()
        {
            return _parent.Query<TValue>(q => _inner);
        }

        public Task<TValue> First()
        {
            return _parent.First<TValue>(q => _inner);
        }

        public Task<TValue> First(Expression<Func<TValue, bool>> filter)
        {
            throw new NotSupportedException();
        }

        public Task<TValue> FirstOrDefault()
        {
            return _parent.FirstOrDefault<TValue>(q => _inner);
        }


        public Task<TValue> Single()
        {
            return _parent.Single<TValue>(q => _inner);
        }

        public Task<TValue> SingleOrDefault()
        {
            return _parent.SingleOrDefault<TValue>(q => _inner);
        }

    }
}