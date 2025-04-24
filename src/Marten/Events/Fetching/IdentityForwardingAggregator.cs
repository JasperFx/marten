
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using Marten.Internal;
using Marten.Internal.Storage;

namespace Marten.Events.Fetching;

internal class IdentityForwardingAggregator<T, TId, TSimple, TSession> : IAggregator<T, TSimple, TSession>
{
    private readonly IAggregator<T, TId, TSession> _inner;
    private readonly IDocumentStorage<T, TId> _storage;
    private readonly Func<TSimple,TId> _wrapper;

    public IdentityForwardingAggregator(IAggregator<T, TId, TSession> inner, ValueTypeIdentifiedDocumentStorage<T, TSimple, TId> storage)
    {
        _inner = inner;
        _storage = storage.Inner;
        _wrapper = ValueTypeInfo.ForType(typeof(TId)).CreateWrapper<TId, TSimple>();
    }

    public ValueTask<T> BuildAsync(IReadOnlyList<IEvent> events, TSession session, T? snapshot, CancellationToken cancellation)
    {
        return _inner.BuildAsync(events, session, snapshot, cancellation);
    }

    public ValueTask<T> BuildAsync(IReadOnlyList<IEvent> events, TSession session, T? snapshot, TSimple id, IIdentitySetter<T, TSimple> identitySetter,
        CancellationToken cancellation)
    {
        return _inner.BuildAsync(events, session, snapshot, _wrapper(id), _storage, cancellation);
    }

    public Type IdentityType => typeof(TId);
}
