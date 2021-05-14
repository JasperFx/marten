using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Events.Archiving;
using Marten.Events.Querying;
using Marten.Exceptions;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Linq;
using Marten.Linq.QueryHandlers;
using Weasel.Postgresql;
using Marten.Schema.Identity;
using Marten.Storage;
using Marten.Util;
using Npgsql;
#nullable enable
namespace Marten.Events
{
    internal class EventStore: IEventStore
    {
        private readonly DocumentSessionBase _session;
        private readonly ITenant _tenant;
        private readonly DocumentStore _store;

        public EventStore(DocumentSessionBase session, DocumentStore store, ITenant tenant)
        {
            _session = session;
            _store = store;
            _tenant = tenant;
        }

        public StreamAction Append(Guid stream, IEnumerable<object> events)
        {
            // NRT: We're ignoring null here as to not unintentionally change any downstream behaviour - Replace with null guards in the future.
            return Append(stream, events?.ToArray()!);
        }

        public StreamAction Append(Guid stream, params object[] events)
        {
            return _store.Events.Append(_session, stream, events);
        }

        public StreamAction Append(string stream, IEnumerable<object> events)
        {
            return Append(stream, events?.ToArray()!);
        }

        public StreamAction Append(string stream, params object[] events)
        {
            return _store.Events.Append(_session, stream, events);
        }

        public StreamAction Append(Guid stream, long expectedVersion, IEnumerable<object> events)
        {
            return Append(stream, expectedVersion, events?.ToArray()!);
        }

        public StreamAction Append(Guid stream, long expectedVersion, params object[] events)
        {
            var eventStream = Append(stream, events);
            eventStream.ExpectedVersionOnServer = expectedVersion - eventStream.Events.Count;

            return eventStream;
        }

        public StreamAction Append(string stream, long expectedVersion, IEnumerable<object> events)
        {
            return Append(stream, expectedVersion, events?.ToArray()!);
        }

        public StreamAction Append(string stream, long expectedVersion, params object[] events)
        {
            var eventStream = Append(stream, events);
            eventStream.ExpectedVersionOnServer = expectedVersion - events.Length;

            return eventStream;
        }

        public StreamAction StartStream<TAggregate>(Guid id, IEnumerable<object> events) where TAggregate : class
        {
            return StartStream<TAggregate>(id, events?.ToArray()!);
        }

        public StreamAction StartStream<T>(Guid id, params object[] events) where T : class
        {
            return StartStream(typeof(T), id, events);
        }

        public StreamAction StartStream(Type aggregateType, Guid id, IEnumerable<object> events)
        {
            return StartStream(aggregateType, id, events?.ToArray()!);
        }

        public StreamAction StartStream(Type aggregateType, Guid id, params object[] events)
        {
            var stream = _store.Events.StartStream(_session, id, events);
            stream.AggregateType = aggregateType;

            return stream;
        }

        public StreamAction StartStream<TAggregate>(string streamKey, IEnumerable<object> events) where TAggregate : class
        {
            return StartStream<TAggregate>(streamKey, events?.ToArray()!);
        }

        public StreamAction StartStream<TAggregate>(string streamKey, params object[] events) where TAggregate : class
        {
            return StartStream(typeof(TAggregate), streamKey, events);
        }

        public StreamAction StartStream(Type aggregateType, string streamKey, IEnumerable<object> events)
        {
            return StartStream(aggregateType, streamKey, events?.ToArray()!);
        }

        public StreamAction StartStream(Type aggregateType, string streamKey, params object[] events)
        {
            var stream = _store.Events.StartStream(_session, streamKey, events);
            stream.AggregateType = aggregateType;

            return stream;
        }

        public StreamAction StartStream(Guid id, IEnumerable<object> events)
        {
            return StartStream(id, events?.ToArray()!);
        }

        public StreamAction StartStream(Guid id, params object[] events)
        {
            return _store.Events.StartStream(_session, id, events);
        }

        public StreamAction StartStream(string streamKey, IEnumerable<object> events)
        {
            return StartStream(streamKey, events?.ToArray()!);
        }

        public StreamAction StartStream(string streamKey, params object[] events)
        {
            return _store.Events.StartStream(_session, streamKey, events);
        }

        public StreamAction StartStream<TAggregate>(IEnumerable<object> events) where TAggregate : class
        {
            return StartStream<TAggregate>(events?.ToArray()!);
        }

        public StreamAction StartStream<TAggregate>(params object[] events) where TAggregate : class
        {
            return StartStream(typeof(TAggregate), events);
        }

        public StreamAction StartStream(Type aggregateType, IEnumerable<object> events)
        {
            return StartStream(aggregateType, events?.ToArray()!);
        }

        public StreamAction StartStream(Type aggregateType, params object[] events)
        {
            return StartStream(aggregateType, CombGuidIdGeneration.NewGuid(), events);
        }

        public StreamAction StartStream(IEnumerable<object> events)
        {
            return StartStream(events?.ToArray()!);
        }

        public StreamAction StartStream(params object[] events)
        {
            return StartStream(CombGuidIdGeneration.NewGuid(), events);
        }

        public IReadOnlyList<IEvent> FetchStream(Guid streamId, long version = 0, DateTime? timestamp = null)
        {
            // TODO -- do this later by just delegating to Load<StreamState>(streamId)
            var selector = _store.Events.EnsureAsGuidStorage(_session);

            var statement = new EventStatement(selector)
            {
                StreamId = streamId, Version = version, Timestamp = timestamp, TenantId = _tenant.TenantId
            };

            IQueryHandler<IReadOnlyList<IEvent>> handler = new ListQueryHandler<IEvent>(statement, selector);

            return _session.ExecuteHandler(handler);
        }

        public Task<IReadOnlyList<IEvent>> FetchStreamAsync(Guid streamId, long version = 0, DateTime? timestamp = null, CancellationToken token = default)
        {
            var selector = _store.Events.EnsureAsGuidStorage(_session);

            var statement = new EventStatement(selector)
            {
                StreamId = streamId, Version = version, Timestamp = timestamp, TenantId = _tenant.TenantId
            };

            IQueryHandler<IReadOnlyList<IEvent>> handler = new ListQueryHandler<IEvent>(statement, selector);

            return _session.ExecuteHandlerAsync(handler, token);
        }

        public IReadOnlyList<IEvent> FetchStream(string streamKey, long version = 0, DateTime? timestamp = null)
        {
            var selector = _store.Events.EnsureAsStringStorage(_session);

            var statement = new EventStatement(selector)
            {
                StreamKey = streamKey, Version = version, Timestamp = timestamp, TenantId = _tenant.TenantId
            };

            IQueryHandler<IReadOnlyList<IEvent>> handler = new ListQueryHandler<IEvent>(statement, selector);

            return _session.ExecuteHandler(handler);
        }

        public Task<IReadOnlyList<IEvent>> FetchStreamAsync(string streamKey, long version = 0, DateTime? timestamp = null, CancellationToken token = default)
        {
            var selector = _store.Events.EnsureAsStringStorage(_session);

            var statement = new EventStatement(selector)
            {
                StreamKey = streamKey, Version = version, Timestamp = timestamp, TenantId = _tenant.TenantId
            };

            IQueryHandler<IReadOnlyList<IEvent>> handler = new ListQueryHandler<IEvent>(statement, selector);

            return _session.ExecuteHandlerAsync(handler, token);
        }

        public T? AggregateStream<T>(Guid streamId, long version = 0, DateTime? timestamp = null, T? state = null) where T : class
        {
            var events = FetchStream(streamId, version, timestamp);

            var aggregator = _store.Options.Projections.AggregatorFor<T>();

            if (!events.Any()) return null;

            var aggregate = aggregator.Build(events, _session, state);

            var storage = _session.StorageFor<T>();
            if (storage is IDocumentStorage<T, Guid> s) s.SetIdentity(aggregate, streamId);

            return aggregate;
        }

        public async Task<T?> AggregateStreamAsync<T>(Guid streamId, long version = 0, DateTime? timestamp = null,
            T? state = null, CancellationToken token = default) where T : class
        {
            var events = await FetchStreamAsync(streamId, version, timestamp, token);
            if (!events.Any()) return null;

            var aggregator = _store.Options.Projections.AggregatorFor<T>();
            var aggregate = await aggregator.BuildAsync(events, _session, state, token);

            if (aggregate == null) return null;

            var storage = _session.StorageFor<T>();
            if (storage is IDocumentStorage<T, Guid> s) s.SetIdentity(aggregate, streamId);

            return aggregate;
        }

        public T? AggregateStream<T>(string streamKey, long version = 0, DateTime? timestamp = null, T? state = null) where T : class
        {
            var events = FetchStream(streamKey, version, timestamp);
            if (!events.Any())
            {
                return null;
            }

            var aggregator = _store.Options.Projections.AggregatorFor<T>();
            var aggregate = aggregator.Build(events, _session, state);

            var storage = _session.StorageFor<T>();
            if (storage is IDocumentStorage<T, string> s) s.SetIdentity(aggregate, streamKey);

            return aggregate;
        }

        public async Task<T?> AggregateStreamAsync<T>(string streamKey, long version = 0, DateTime? timestamp = null,
            T? state = null, CancellationToken token = default) where T : class
        {
            var events = await FetchStreamAsync(streamKey, version, timestamp, token);
            if (!events.Any())
            {
                return null;
            }

            var aggregator = _store.Options.Projections.AggregatorFor<T>();

            var aggregate = await aggregator.BuildAsync(events, _session, state, token);

            var storage = _session.StorageFor<T>();
            if (storage is IDocumentStorage<T, string> s) s.SetIdentity(aggregate, streamKey);

            return aggregate;
        }

        public IMartenQueryable<T> QueryRawEventDataOnly<T>()
        {
            _tenant.EnsureStorageExists(typeof(StreamAction));

            _store.Events.AddEventType(typeof(T));

            return _session.Query<T>();
        }

        public IMartenQueryable<IEvent> QueryAllRawEvents()
        {
            _tenant.EnsureStorageExists(typeof(StreamAction));

            return _session.Query<IEvent>();
        }

        public IEvent<T> Load<T>(Guid id) where T : class
        {
            _tenant.EnsureStorageExists(typeof(StreamAction));

            _store.Events.AddEventType(typeof(T));

            return Load(id).As<Event<T>>();
        }

        public async Task<IEvent<T>> LoadAsync<T>(Guid id, CancellationToken token = default) where T : class
        {
            _tenant.EnsureStorageExists(typeof(StreamAction));

            _store.Events.AddEventType(typeof(T));

            return (await LoadAsync(id, token)).As<Event<T>>();
        }

        public IEvent Load(Guid id)
        {
            var handler = new SingleEventQueryHandler(id, _session.EventStorage());
            return _session.ExecuteHandler(handler);
        }

        public Task<IEvent> LoadAsync(Guid id, CancellationToken token = default)
        {
            _tenant.EnsureStorageExists(typeof(StreamAction));

            var handler = new SingleEventQueryHandler(id, _session.EventStorage());
            return _session.ExecuteHandlerAsync(handler, token);
        }

        public StreamState FetchStreamState(Guid streamId)
        {
            var handler = _tenant.EventStorage().QueryForStream(StreamAction.ForReference(streamId, _tenant));
            return _session.ExecuteHandler(handler);
        }

        public Task<StreamState> FetchStreamStateAsync(Guid streamId, CancellationToken token = default)
        {
            var handler = _tenant.EventStorage().QueryForStream(StreamAction.ForReference(streamId, _tenant));
            return _session.ExecuteHandlerAsync(handler, token);
        }

        public StreamState FetchStreamState(string streamKey)
        {
            var handler = _tenant.EventStorage().QueryForStream(StreamAction.ForReference(streamKey, _tenant));
            return _session.ExecuteHandler(handler);
        }

        public Task<StreamState> FetchStreamStateAsync(string streamKey, CancellationToken token = default)
        {
            var handler = _tenant.EventStorage().QueryForStream(StreamAction.ForReference(streamKey, _tenant));
            return _session.ExecuteHandlerAsync(handler, token);
        }

        public async Task AppendOptimistic(string streamKey, CancellationToken token, params object[] events)
        {
            _store.Events.EnsureAsStringStorage(_session);

            // TODO memoize this
            var cmd = new NpgsqlCommand($"select version from {_store.Events.DatabaseSchemaName}.mt_streams where id = :id")
                .With("id", streamKey);

            var version = await readVersionFromExistingStream(streamKey, token, cmd);

            var action = Append(streamKey, events);
            action.ExpectedVersionOnServer = version;
        }

        private async Task<long> readVersionFromExistingStream(object streamId, CancellationToken token, NpgsqlCommand cmd)
        {
            long version = 0;
            try
            {
                using var reader = await _session.Database.ExecuteReaderAsync(cmd, token);
                if (await reader.ReadAsync(token))
                {
                    version = await reader.GetFieldValueAsync<long>(0, token);
                }
            }
            catch (Exception e)
            {
                if (e.Message.Contains(MartenCommandException.MaybeLockedRowsMessage))
                {
                    throw new StreamLockedException(streamId, e.InnerException);
                }

                throw;
            }

            if (version == 0)
            {
                throw new NonExistentStreamException(streamId);
            }

            return version;
        }

        public Task AppendOptimistic(string streamKey, params object[] events)
        {
            return AppendOptimistic(streamKey, CancellationToken.None, events);
        }

        public async Task AppendOptimistic(Guid streamId, CancellationToken token, params object[] events)
        {
            _store.Events.EnsureAsGuidStorage(_session);

            var cmd = new NpgsqlCommand($"select version from {_store.Events.DatabaseSchemaName}.mt_streams where id = :id")
                .With("id", streamId);

            var version = await readVersionFromExistingStream(streamId, token, cmd);

            var action = Append(streamId, events);
            action.ExpectedVersionOnServer = version;
        }

        public Task AppendOptimistic(Guid streamId, params object[] events)
        {
            return AppendOptimistic(streamId, CancellationToken.None, events);
        }

        public async Task AppendExclusive(string streamKey, CancellationToken token, params object[] events)
        {
            _store.Events.EnsureAsStringStorage(_session);

            var cmd = new NpgsqlCommand($"select version from {_store.Events.DatabaseSchemaName}.mt_streams where id = :id for update")
                .With("id", streamKey);

            await _session.Database.BeginTransactionAsync(token);

            var version = await readVersionFromExistingStream(streamKey, token, cmd);

            var action = Append(streamKey, events);
            action.ExpectedVersionOnServer = version;
        }

        public Task AppendExclusive(string streamKey, params object[] events)
        {
            return AppendExclusive(streamKey, CancellationToken.None, events);
        }

        public async Task AppendExclusive(Guid streamId, CancellationToken token, params object[] events)
        {
            _store.Events.EnsureAsGuidStorage(_session);

            var cmd = new NpgsqlCommand($"select version from {_store.Events.DatabaseSchemaName}.mt_streams where id = :id for update")
                .With("id", streamId);

            await _session.Database.BeginTransactionAsync(token);

            var version = await readVersionFromExistingStream(streamId, token, cmd);

            var action = Append(streamId, events);
            action.ExpectedVersionOnServer = version;
        }

        public Task AppendExclusive(Guid streamId, params object[] events)
        {
            return AppendExclusive(streamId, CancellationToken.None, events);
        }

        public void ArchiveStream(Guid streamId)
        {
            var op = new ArchiveStreamOperation(_store.Events, streamId);
            _session.QueueOperation(op);
        }

        public void ArchiveStream(string streamKey)
        {
            var op = new ArchiveStreamOperation(_store.Events, streamKey);
            _session.QueueOperation(op);
        }
    }
}
