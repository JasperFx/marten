#nullable enable
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten.Linq;

namespace Marten.Services.BatchQuerying;

public interface ITransformedBatchQueryable<TValue>
{
    Task<IReadOnlyList<TValue>> ToList();

    Task<TValue> First();

    Task<TValue?> FirstOrDefault();

    Task<TValue> Single();

    Task<TValue?> SingleOrDefault();
}

internal class TransformedBatchQueryable<TValue>: ITransformedBatchQueryable<TValue>
{
    private readonly IMartenQueryable<TValue> _inner;
    private readonly BatchedQuery _parent;

    public TransformedBatchQueryable(BatchedQuery parent, IMartenQueryable<TValue> inner)
    {
        _parent = parent;
        _inner = inner;
    }

    public Task<IReadOnlyList<TValue>> ToList()
    {
        return _parent.Query(_inner);
    }

    public Task<TValue> First()
    {
        return _parent.First(_inner);
    }

    public Task<TValue?> FirstOrDefault()
    {
        return _parent.FirstOrDefault(_inner);
    }

    public Task<TValue> Single()
    {
        return _parent.Single(_inner);
    }

    public Task<TValue?> SingleOrDefault()
    {
        return _parent.SingleOrDefault(_inner);
    }

    public Task<TValue> First(Expression<Func<TValue, bool>> filter)
    {
        throw new NotSupportedException();
    }
}
