using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Archiving;
using Marten.Exceptions;
using Marten.Internal.Sessions;
using Marten.Schema.Identity;
using Marten.Storage;
using Npgsql;
using Weasel.Core;

#nullable enable
namespace Marten.Events
{
    internal class EventStore: QueryEventStore, IEventStore
    {
        private readonly DocumentSessionBase _session;
        private readonly Tenant _tenant;
        private readonly DocumentStore _store;

        public EventStore(DocumentSessionBase session, DocumentStore store, Tenant tenant) : base(session, store, tenant)
        {
            _session = session;
            _store = store;
            _tenant = tenant;
        }

        public StreamAction Append(Guid stream, IEnumerable<object> events)
        {
            //TODO NRT: We're ignoring null here as to not unintentionally change any downstream behaviour - Replace with null guards in the future.
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

        public async Task AppendOptimistic(string streamKey, CancellationToken token, params object[] events)
        {
            _store.Events.EnsureAsStringStorage(_session);
            await _session.Database.EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

            var cmd = new NpgsqlCommand($"select version from {_store.Events.DatabaseSchemaName}.mt_streams where id = :id")
                .With("id", streamKey);

            var version = await readVersionFromExistingStream(streamKey, token, cmd).ConfigureAwait(false);

            var action = Append(streamKey, events);
            action.ExpectedVersionOnServer = version;
        }

        private async Task<long> readVersionFromExistingStream(object streamId, CancellationToken token, NpgsqlCommand cmd)
        {
            long version = 0;
            try
            {
                using var reader = await _session.ExecuteReaderAsync(cmd, token).ConfigureAwait(false);
                if (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    version = await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);
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
            await _session.Database.EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

            var cmd = new NpgsqlCommand($"select version from {_store.Events.DatabaseSchemaName}.mt_streams where id = :id")
                .With("id", streamId);

            var version = await readVersionFromExistingStream(streamId, token, cmd).ConfigureAwait(false);

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
            await _session.Database.EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

            var cmd = new NpgsqlCommand($"select version from {_store.Events.DatabaseSchemaName}.mt_streams where id = :id for update")
                .With("id", streamKey);

            await _session.BeginTransactionAsync(token).ConfigureAwait(false);

            var version = await readVersionFromExistingStream(streamKey, token, cmd).ConfigureAwait(false);

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
            await _session.Database.EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

            var cmd = new NpgsqlCommand($"select version from {_store.Events.DatabaseSchemaName}.mt_streams where id = :id for update")
                .With("id", streamId);

            await _session.BeginTransactionAsync(token).ConfigureAwait(false);

            var version = await readVersionFromExistingStream(streamId, token, cmd).ConfigureAwait(false);

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

        public void ApplyHeader(string key, object? value, params object[] events)
        {
            foreach (var @event in events)
            {
                var overrides = _session.GetOrCreateEventMetadataOverrides(@event);

                if (value is null && overrides.Headers is null)
                {
                    continue;
                }
                
                overrides.Headers ??= new Dictionary<string, object>();

                if (value is null)
                {
                    overrides.Headers.Remove(key);
                    return;
                }

                overrides.Headers.Add(key, value);
            }
        }

        public void ApplyHeaders(IDictionary<string, object> headers, params object[] events)
        {
            foreach (var @event in events)
            {
                var overrides = _session.GetOrCreateEventMetadataOverrides(@event);

                if (overrides.Headers is null)
                {
                    overrides.Headers = new Dictionary<string, object>(headers);
                    continue;
                }

                foreach (var header in headers)
                {
                    overrides.Headers.Add(header.Key, header.Value);
                }
            }
        }

        public void ApplyCorrelationId(string? correlationId, params object[] events)
        {
            foreach (var @event in events)
            {
                var overrides = _session.GetOrCreateEventMetadataOverrides(@event);
                overrides.CorrelationId = correlationId;
            }
        }

        public void ApplyCausationId(string? causationId, params object[] events)
        {
            foreach (var @event in events)
            {
                var overrides = _session.GetOrCreateEventMetadataOverrides(@event);
                overrides.CausationId = causationId;
            }
        }

        public void CopyMetadata(IEventMetadata metadata, object @event)
        {
            var overrides = _session.GetOrCreateEventMetadataOverrides(@event);
            overrides.CorrelationId = metadata.CorrelationId ?? overrides.CorrelationId;
            overrides.CausationId = metadata.CausationId ?? overrides.CausationId;

            if (metadata.Headers is null)
            {
                return;
            }

            if (overrides.Headers is null)
            {
                overrides.Headers = new Dictionary<string, object>(metadata.Headers);
                return;
            }

            foreach (var header in metadata.Headers)
            {
                overrides.Headers.Add(header.Key, header.Value);
            }
        }
    }
}
