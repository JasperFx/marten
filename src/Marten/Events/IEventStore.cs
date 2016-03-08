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

        ITransforms Transforms { get; }

        StreamState FetchStreamState(Guid streamId);
    }

    public interface ITransforms
    {
        TTarget TransformTo<TEvent, TTarget>(Guid stream, TEvent @event) where TEvent : IEvent;
        string Transform(string projectionName, Guid stream, IEvent @event) ;

        TAggregate ApplySnapshot<TAggregate>(TAggregate aggregate, IEvent @event) where TAggregate : IAggregate;

        T ApplyProjection<T>(string projectionName, T aggregate, IEvent @event) where T : IAggregate;
        TAggregate StartSnapshot<TAggregate>(IEvent @event) where TAggregate : IAggregate;
    }

    public interface IEventStoreAdmin
    {
        void LoadProjections(string directory);

        void LoadProjection(string file);

        void ClearAllProjections();

        IEnumerable<ProjectionUsage> InitializeEventStoreInDatabase(bool overwrite = false);

        IEnumerable<ProjectionUsage> ProjectionUsages(); 

        void RebuildEventStoreSchema();

    }

    public class StreamState
    {
        public Guid Id { get; }
        public int Version { get; }
        public Type AggregateType { get; }

        public StreamState(Guid id, int version, Type aggregateType)
        {
            Id = id;
            Version = version;
            AggregateType = aggregateType;
        }
    }
}