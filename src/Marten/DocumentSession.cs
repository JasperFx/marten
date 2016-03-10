using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Events;
using Marten.Linq;
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

        public DocumentSession(IDocumentStore store, StoreOptions options, IDocumentSchema schema, ISerializer serializer, IManagedConnection connection, IQueryParser parser, IIdentityMap identityMap) : base(store, schema, serializer, connection, parser, identityMap, options)
        {
            _options = options;
            _schema = schema;
            _serializer = serializer;
            _connection = connection;

            _identityMap = identityMap;


            _unitOfWork = new UnitOfWork(_schema, Parser);

            if (_identityMap is IDocumentTracker)
            {
                _unitOfWork.AddTracker(_identityMap.As<IDocumentTracker>());
            }

            Events = new EventStore(this, _identityMap, schema, _serializer, _connection);

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

        public void DeleteWhere<T>(Expression<Func<T, bool>> expression)
        {
            _unitOfWork.Delete<T>(expression);
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
                    bool assigned = false;
                    var id = idAssignment.Assign(entity, out assigned);
                    // TODO -- categorize as insert or update on UnitOfWork

                    storage.Store(_identityMap, id, entity);
                    if (assigned)
                    {
                        _unitOfWork.StoreInserts(entity);
                    }
                    else
                    {
                        _unitOfWork.StoreUpdates(entity);
                    }
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
            var changes = _unitOfWork.ApplyChanges(batch);
            _changes.Add(changes);


            _connection.Commit();

            Logger.RecordSavedChanges(this);

            _options.Listeners.Each(x => x.AfterCommit(this));
        }

        public async Task SaveChangesAsync(CancellationToken token)
        {
            foreach (var listener in _options.Listeners)
            {
                await listener.BeforeSaveChangesAsync(this);
            }


            var batch = new UpdateBatch(_options, _serializer, _connection);
            var changes = await _unitOfWork.ApplyChangesAsync(batch, token);

            _changes.Add(changes);

            _connection.Commit();

            Logger.RecordSavedChanges(this);

            foreach (var listener in _options.Listeners)
            {
                await listener.AfterCommitAsync(this);
            }
        }

        private readonly IList<IChangeSet> _changes = new List<IChangeSet>();

        public IEnumerable<IChangeSet> Commits => _changes;
        public IChangeSet LastCommit => _changes.LastOrDefault();

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
}