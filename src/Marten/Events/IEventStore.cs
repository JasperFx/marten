using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Marten.Linq;

namespace Marten.Events
{
    public interface IEventStore
    {
        void Append(Guid stream, object @event);

        void AppendEvents(Guid stream, params object[] events);

        Guid StartStream<TAggregate>(Guid id, params object[] events) where TAggregate : class, new();
        Guid StartStream<TAggregate>(params object[] events) where TAggregate : class, new();

        TAggregate FetchSnapshot<TAggregate>(Guid streamId) where TAggregate : class, new();

        IEnumerable<IEvent> FetchStream(Guid streamId);
        IEnumerable<IEvent> FetchStream(Guid streamId, int version);
        IEnumerable<IEvent> FetchStream(Guid streamId, DateTime timestamp);

        

        void DeleteEvent(Guid id);
        void DeleteEvent(IEvent @event);


        void ReplaceEvent<T>(T @event);

        ITransforms Transforms { get; }

        StreamState FetchStreamState(Guid streamId);

        IMartenQueryable<T> Query<T>();
        T Load<T>(Guid id) where T : class;
        Task<T> LoadAsync<T>(Guid id) where T : class;
    }

    public interface ITransforms
    {
        TAggregate TransformTo<TAggregate>(Guid streamId, IEvent @event);
        string Transform(string projectionName, Guid streamId, IEvent @event) ;

        TAggregate ApplySnapshot<TAggregate>(Guid streamId, TAggregate aggregate, IEvent @event) where TAggregate : class, new();

        TAggregate ApplyProjection<TAggregate>(string projectionName, TAggregate aggregate, IEvent @event) where TAggregate : class, new();
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