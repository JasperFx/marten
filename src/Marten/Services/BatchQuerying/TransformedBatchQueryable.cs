using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten.Linq;
#nullable enable
namespace Marten.Services.BatchQuerying
{
    public interface ITransformedBatchQueryable<TValue>
    {
        Task<IReadOnlyList<TValue>> ToList();

        Task<TValue> First();

        Task<TValue?> FirstOrDefault();

        Task<TValue> Single();

        Task<TValue?> SingleOrDefault();
    }

    public class TransformedBatchQueryable<TValue>: ITransformedBatchQueryable<TValue>
    {
        private readonly BatchedQuery _parent;
        private readonly IMartenQueryable<TValue> _inner;

        public TransformedBatchQueryable(BatchedQuery parent, IMartenQueryable<TValue> inner)
        {
            _parent = parent;
            _inner = inner;
        }

        public Task<IReadOnlyList<TValue>> ToList()
        {
            return _parent.Query<TValue>(_inner);
        }

        public Task<TValue> First()
        {
            return _parent.First<TValue>(_inner);
        }

        public Task<TValue> First(Expression<Func<TValue, bool>> filter)
        {
            throw new NotSupportedException();
        }

        public Task<TValue?> FirstOrDefault()
        {
            return _parent.FirstOrDefault<TValue>(_inner);
        }

        public Task<TValue> Single()
        {
            return _parent.Single<TValue>(_inner);
        }

        public Task<TValue?> SingleOrDefault()
        {
            return _parent.SingleOrDefault<TValue>(_inner);
        }
    }
}
