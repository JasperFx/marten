using System;
using System.Collections.Generic;

namespace Marten.Events
{
    public interface IEventStore
    {
        void Append<T>(Guid stream, T @event) where T : IEvent;

        void AppendEvents(Guid stream, params IEvent[] events);

        Guid StartStream<T>(Guid id, params IEvent[] events) where T : class, new();
        Guid StartStream<T>(params IEvent[] events) where T : class, new();

        T FetchSnapshot<T>(Guid streamId) where T : class, new();

        IEnumerable<IEvent> FetchStream(Guid streamId);
        IEnumerable<IEvent> FetchStream(Guid streamId, int version);
        IEnumerable<IEvent> FetchStream(Guid streamId, DateTime timestamp);

        

        void DeleteEvent<T>(Guid id);
        void DeleteEvent<T>(T @event) where T : IEvent;


        void ReplaceEvent<T>(T @event);

        ITransforms Transforms { get; }

        StreamState FetchStreamState(Guid streamId);
        
    }

    public interface ITransforms
    {
        TTarget TransformTo<TEvent, TTarget>(Guid streamId, TEvent @event) where TEvent : IEvent;
        string Transform(string projectionName, Guid streamId, IEvent @event) ;

        TAggregate ApplySnapshot<TAggregate>(Guid streamId, TAggregate aggregate, IEvent @event) where TAggregate : class, new();

        T ApplyProjection<T>(string projectionName, T aggregate, IEvent @event) where T : class, new();
        TAggregate StartSnapshot<TAggregate>(Guid streamId, IEvent @event) where TAggregate : class, new();
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