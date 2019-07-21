using System;
using System.Collections.Generic;

namespace Marten.Events.Projections
{
    internal interface IEventIdsSelector<TId>
    {
        List<TId> Select(IDocumentSession session, object @event, Guid aggregateId);
    }

    internal class SingleEventIdsSelector<TId>: IEventIdsSelector<TId>
    {
        private readonly Func<IDocumentSession, object, Guid, TId> IdSelector;

        public SingleEventIdsSelector(Func<IDocumentSession, object, Guid, TId> idSelector)
        {
            IdSelector = idSelector ?? throw new ArgumentNullException(nameof(idSelector));
        }

        public List<TId> Select(IDocumentSession session, object @event, Guid aggregateId)
        {
            return new List<TId> { IdSelector(session, @event, aggregateId) };
        }
    }

    internal class MultipleEventIdsSelector<TId>: IEventIdsSelector<TId>
    {
        private readonly Func<IDocumentSession, object, Guid, List<TId>> IdsSelector;

        public MultipleEventIdsSelector(Func<IDocumentSession, object, Guid, List<TId>> idsSelector)
        {
            IdsSelector = idsSelector ?? throw new ArgumentNullException(nameof(idsSelector));
        }

        public List<TId> Select(IDocumentSession session, object @event, Guid aggregateId)
        {
            return IdsSelector(session, @event, aggregateId);
        }
    }

    internal static class IdsSelector
    {
        public static SingleEventIdsSelector<TId> Create<TId>(Func<IDocumentSession, object, Guid, TId> idSelector)
            => new SingleEventIdsSelector<TId>(idSelector);

        public static SingleEventIdsSelector<TId> Create<TEvent, TId>(Func<IDocumentSession, TEvent, Guid, TId> idSelector)
            => new SingleEventIdsSelector<TId>((session, @event, streamId) => idSelector(session, (TEvent)@event, streamId));

        public static SingleEventIdsSelector<TId> Create<TId>(Func<object, Guid, TId> idSelector)
            => new SingleEventIdsSelector<TId>((_, @event, streamId) => idSelector(@event, streamId));

        public static SingleEventIdsSelector<TId> Create<TEvent, TId>(Func<TEvent, Guid, TId> idSelector)
            => new SingleEventIdsSelector<TId>((_, @event, streamId) => idSelector((TEvent)@event, streamId));

        internal static SingleEventIdsSelector<TId> Create<TEvent, TId>(Func<TEvent, TId> idSelector)
            => new SingleEventIdsSelector<TId>((_, @event, streamId) => idSelector((TEvent)@event));

        internal static SingleEventIdsSelector<TId> Create<TEvent, TId>(Func<IDocumentSession, TEvent, TId> idSelector)
            => new SingleEventIdsSelector<TId>((session, @event, streamId) => idSelector(session, (TEvent)@event));

        public static MultipleEventIdsSelector<TId> Create<TId>(Func<IDocumentSession, object, Guid, List<TId>> idsSelector)
            => new MultipleEventIdsSelector<TId>(idsSelector);

        public static MultipleEventIdsSelector<TId> Create<TEvent, TId>(Func<IDocumentSession, TEvent, Guid, List<TId>> idsSelector)
            => new MultipleEventIdsSelector<TId>((session, @event, streamId) => idsSelector(session, (TEvent)@event, streamId));

        public static MultipleEventIdsSelector<TId> Create<TId>(Func<object, Guid, List<TId>> idSelector)
            => new MultipleEventIdsSelector<TId>((_, @event, streamId) => idSelector(@event, streamId));

        public static MultipleEventIdsSelector<TId> Create<TEvent, TId>(Func<TEvent, Guid, List<TId>> idSelector)
            => new MultipleEventIdsSelector<TId>((_, @event, streamId) => idSelector((TEvent)@event, streamId));

        internal static MultipleEventIdsSelector<TId> Create<TEvent, TId>(Func<TEvent, List<TId>> idsSelector)
            => new MultipleEventIdsSelector<TId>((_, @event, streamId) => idsSelector((TEvent)@event));

        internal static MultipleEventIdsSelector<TId> Create<TEvent, TId>(Func<IDocumentSession, TEvent, List<TId>> idsSelector)
            => new MultipleEventIdsSelector<TId>((session, @event, streamId) => idsSelector(session, (TEvent)@event));
    }
}
