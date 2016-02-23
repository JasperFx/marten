using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Events;
using Marten.Schema;
using Marten.Services;
using Remotion.Linq.Parsing.Structure;

namespace Marten
{
    public class DocumentSession : QuerySession, IDocumentSession
    {
        private readonly IIdentityMap _identityMap;
        private readonly IManagedConnection _connection;
        private readonly ISerializer _serializer;
        private readonly StoreOptions _options;
        private readonly IDocumentSchema _schema;
        private readonly UnitOfWork _unitOfWork;

        public DocumentSession(StoreOptions options, IDocumentSchema schema, ISerializer serializer, IManagedConnection connection, IQueryParser parser, IIdentityMap identityMap) : base(schema, serializer, connection, parser, identityMap)
        {
            _options = options;
            _schema = schema;
            _serializer = serializer;
            _connection = connection;

            _identityMap = identityMap;
            _unitOfWork = new UnitOfWork(_schema);

            if (_identityMap is IDocumentTracker)
            {
                _unitOfWork.AddTracker(_identityMap.As<IDocumentTracker>());
            }

            Events = new EventStore(this, _identityMap, schema);

        }

        public void Delete<T>(T entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            _unitOfWork.DeleteEntity(entity);

            var storage = _schema.StorageFor(typeof (T));
            storage.Remove(_identityMap, entity);
        }

        public void Delete<T>(ValueType id)
        {
            _unitOfWork.Delete<T>(id);

            var storage = _schema.StorageFor(typeof(T));
            storage.Delete(_identityMap, id);
        }

        public void Delete<T>(string id)
        {
            _unitOfWork.Delete<T>(id);

            var storage = _schema.StorageFor(typeof(T));
            storage.Delete(_identityMap, id);
        }

        public void Store<T>(params T[] entities) 
        {
            if (entities == null) throw new ArgumentNullException(nameof(entities));

            if (typeof (T) == typeof (object))
            {
                StoreObjects(entities.OfType<object>());
            }
            else
            {
                var storage = _schema.StorageFor(typeof(T));
                var idAssignment = storage.As<IdAssignment<T>>();

                foreach (var entity in entities)
                {
                    var id = idAssignment.Assign(entity);

                    storage.Store(_identityMap, id, entity);
                    _unitOfWork.Store(entity);
                }
            }


        }

        public IUnitOfWork PendingChanges => _unitOfWork;
        public void StoreObjects(IEnumerable<object> documents)
        {
            documents.Where(x => x != null).GroupBy(x => x.GetType()).Each(group =>
            {
                var handler = typeof (Handler<>).CloseAndBuildAs<IHandler>(group.Key);
                handler.Store(this, group);
            });
        }

        public IEventStore Events { get; }

        public void SaveChanges()
        {
            _options.Listeners.Each(x => x.BeforeSaveChanges(this));

            var batch = new UpdateBatch(_options, _serializer, _connection);
            _unitOfWork.ApplyChanges(batch);


            _connection.Commit();

            _options.Listeners.Each(x => x.AfterCommit(this));
        }

        public async Task SaveChangesAsync(CancellationToken token)
        {
            foreach (var listener in _options.Listeners)
            {
                await listener.BeforeSaveChangesAsync(this);
            }


            var batch = new UpdateBatch(_options, _serializer, _connection);
            await _unitOfWork.ApplyChangesAsync(batch, token);

            _connection.Commit();

            foreach (var listener in _options.Listeners)
            {
                await listener.AfterCommitAsync(this);
            }
        }

        internal interface IHandler
        {
            void Store(IDocumentSession session, IEnumerable<object> objects);
        }

        internal class Handler<T> : IHandler
        {
            public void Store(IDocumentSession session, IEnumerable<object> objects)
            {
                session.Store(objects.OfType<T>().ToArray());
            }
        }
    }

    public class EventStore : IEventStore
    {
        private readonly IDocumentSession _session;
        private readonly IIdentityMap _identityMap;
        private readonly IDocumentSchema _schema;

        public EventStore(IDocumentSession session, IIdentityMap identityMap, IDocumentSchema schema)
        {
            _session = session;
            _identityMap = identityMap;
            _schema = schema;
        }

        public void Append<T>(Guid stream, T @event) where T : IEvent
        {
            throw new NotImplementedException();
        }

        public void AppendEvents(Guid stream, params IEvent[] events)
        {
            throw new NotImplementedException();
        }

        public Guid StartStream<T>(params IEvent[] events) where T : IAggregate
        {
            throw new NotImplementedException();
        }

        public T FetchSnapshot<T>(Guid streamId) where T : IAggregate
        {
            throw new NotImplementedException();
        }

        public IEnumerable<IEvent> FetchStream<T>(Guid streamId) where T : IAggregate
        {
            throw new NotImplementedException();
        }

        public void DeleteEvent<T>(Guid id)
        {
            throw new NotImplementedException();
        }

        public void DeleteEvent<T>(T @event) where T : IEvent
        {
            throw new NotImplementedException();
        }

        public void ReplaceEvent<T>(T @event)
        {
            throw new NotImplementedException();
        }
    }
}