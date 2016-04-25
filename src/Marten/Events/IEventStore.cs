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

        IList<IEvent> FetchStream(Guid streamId, int version = 0, DateTime? timestamp = null);
        Task<IList<IEvent>> FetchStreamAsync(Guid streamId, int version = 0, DateTime? timestamp = null, CancellationToken token = default(CancellationToken));

        T AggregateStream<T>(Guid streamId, int version = 0, DateTime? timestamp = null) where T : class, new();
        Task<T> AggregateStreamAsync<T>(Guid streamId, int version = 0, DateTime? timestamp = null, CancellationToken token = default(CancellationToken)) where T : class, new();

        ITransforms Transforms { get; }


        IMartenQueryable<T> Query<T>();

        Event<T> Load<T>(Guid id) where T : class;
        Task<Event<T>> LoadAsync<T>(Guid id, CancellationToken token = default(CancellationToken)) where T : class;

        IEvent Load(Guid id);
        Task<IEvent> LoadAsync(Guid id, CancellationToken token = default(CancellationToken));

        StreamState FetchStreamState(Guid streamId);
        Task<StreamState> FetchStreamStateAsync(Guid streamId, CancellationToken token = default(CancellationToken));
    }

    public interface ITransforms
    {
        string Transform(string projectionName, Guid streamId, IEvent @event);

        TAggregate ApplySnapshot<TAggregate>(Guid streamId, TAggregate aggregate, IEvent @event)
            where TAggregate : class, new();

        TAggregate StartSnapshot<TAggregate>(Guid streamId, IEvent @event) where TAggregate : class, new();
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