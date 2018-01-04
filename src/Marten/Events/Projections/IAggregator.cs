using System;
using System.Collections.Generic;

namespace Marten.Events.Projections
{
    public interface IAggregator
    {
        Type AggregateType { get; }
        string Alias { get; }
        bool AppliesTo(EventStream stream);
    }

    public interface IAggregator<T> : IAggregator
    {
        IAggregation<T, TEvent> AggregatorFor<TEvent>();
        T Build(IEnumerable<IEvent> events, IDocumentSession session);
        T Build(IEnumerable<IEvent> events, IDocumentSession session, T state);

        Type[] EventTypes { get; }
    }
}