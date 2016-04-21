using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;

namespace Marten.Events
{
    public interface IEventStore
    {
        void Append(Guid stream, params object[] events);

        Guid StartStream<TAggregate>(Guid id, params object[] events) where TAggregate : class, new();
        Guid StartStream<TAggregate>(params object[] events) where TAggregate : class, new();

        IEnumerable<IEvent> FetchStream(Guid streamId);
        IEnumerable<IEvent> FetchStream(Guid streamId, int version);
        IEnumerable<IEvent> FetchStream(Guid streamId, DateTime timestamp);

        T AggregateStream<T>(Guid streamId) where T : class, new();

        ITransforms Transforms { get; }


        IMartenQueryable<T> Query<T>();
        T Load<T>(Guid id) where T : class;
        Task<T> LoadAsync<T>(Guid id) where T : class;
        StreamState FetchStreamState(Guid streamId);
        
    }

    public interface ITransforms
    {
        string Transform(string projectionName, Guid streamId, IEvent @event) ;

        TAggregate ApplySnapshot<TAggregate>(Guid streamId, TAggregate aggregate, IEvent @event) where TAggregate : class, new();

        TAggregate StartSnapshot<TAggregate>(Guid streamId, IEvent @event) where TAggregate : class, new();

    }

    [Obsolete("Replace this with just EventStream")]
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

    public interface IEventStoreAdmin
    {
        void LoadProjections(string directory);

        void LoadProjection(string file);

        void ClearAllProjections();

        IEnumerable<ProjectionUsage> InitializeEventStoreInDatabase(bool overwrite = false);

        IEnumerable<ProjectionUsage> ProjectionUsages(); 

        void RebuildEventStoreSchema();

    }


}