using System;
using System.Collections.Generic;

namespace Marten.Events
{
    public interface IEventStore
    {
        void Append<T>(Guid stream, T @event) where T : IEvent;

        void AppendEvents(Guid stream, params IEvent[] events);

        Guid StartStream<T>(params IEvent[] events) where T : IAggregate;

        T FetchSnapshot<T>(Guid streamId) where T : IAggregate;

        IEnumerable<IEvent> FetchStream<T>(Guid streamId) where T : IAggregate;

        void DeleteEvent<T>(Guid id);
        void DeleteEvent<T>(T @event) where T : IEvent;


        void ReplaceEvent<T>(T @event);

        IEventStoreAdmin Administration { get; }
    }

    public interface IEventStoreAdmin
    {
        void LoadProjections(string directory);

        void ClearAllProjections();

        IEnumerable<ProjectionUsage> ProjectionUsages();

        void RebuildEventStoreSchema();

    }
}