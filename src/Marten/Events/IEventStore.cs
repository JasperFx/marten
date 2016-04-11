using System;
using System.Collections.Generic;

namespace Marten.Events
{
    public interface IEventStore
    {
        void Append(Guid stream, object @event);

        void AppendEvents(Guid stream, params object[] events);

        Guid StartStream<TAggregate>(Guid id, params object[] events) where TAggregate : class, new();
        Guid StartStream<TAggregate>(params object[] events) where TAggregate : class, new();

        TAggregate FetchSnapshot<TAggregate>(Guid streamId) where TAggregate : class, new();

        IEnumerable<Event> FetchStream(Guid streamId);
        IEnumerable<Event> FetchStream(Guid streamId, int version);
        IEnumerable<Event> FetchStream(Guid streamId, DateTime timestamp);

        

        void DeleteEvent(Guid id);
        void DeleteEvent(Event @event);


        void ReplaceEvent<T>(T @event);

        ITransforms Transforms { get; }

        StreamState FetchStreamState(Guid streamId);
        
    }

    public interface ITransforms
    {
        TAggregate TransformTo<TAggregate>(Guid streamId, Event @event);
        string Transform(string projectionName, Guid streamId, Event @event) ;

        TAggregate ApplySnapshot<TAggregate>(Guid streamId, TAggregate aggregate, Event @event) where TAggregate : class, new();

        TAggregate ApplyProjection<TAggregate>(string projectionName, TAggregate aggregate, Event @event) where TAggregate : class, new();
        TAggregate StartSnapshot<TAggregate>(Guid streamId, Event @event) where TAggregate : class, new();
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