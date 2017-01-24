using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Events;
using Marten.Linq;
using Marten.Patching;
using Marten.Schema;
using Marten.Services;
using Remotion.Linq.Parsing.Structure;

namespace Marten
{
    public class DocumentSession : QuerySession, IDocumentSession
    {
        private readonly IManagedConnection _connection;
        private readonly StoreOptions _options;
        private readonly IDocumentSchema _schema;
        private readonly ISerializer _serializer;
        private readonly UnitOfWork _unitOfWork;

        public DocumentSession(IDocumentStore store, StoreOptions options, IDocumentSchema schema,
            ISerializer serializer, IManagedConnection connection, IQueryParser parser, IIdentityMap identityMap, CharArrayTextWriter.Pool writerPool)
            : base(store, schema, serializer, connection, parser, identityMap, writerPool)
        {
            _options = options;
            _schema = schema;
            _serializer = serializer;
            _connection = connection;

            IdentityMap = identityMap;


            _unitOfWork = new UnitOfWork(_schema);

            if (IdentityMap is IDocumentTracker)
            {
                _unitOfWork.AddTracker(IdentityMap.As<IDocumentTracker>());
            }

            Events = new EventStore(this, schema, _serializer, _connection, _unitOfWork);
        }

        // This is here for testing purposes, not part of IDocumentSession
        public IIdentityMap IdentityMap { get; }

        public void Delete<T>(T entity)
        {
            assertNotDisposed();

            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var storage = _schema.StorageFor(typeof(T));
            var deletion = storage.DeletionForEntity(entity);

            _unitOfWork.Add(deletion);

            storage.Remove(IdentityMap, entity);
        }

        public void Delete<T>(int id)
        {
            delete<T>(id);
        }

        private void delete<T>(object id)
        {
            assertNotDisposed();
            var storage = _schema.StorageFor(typeof(T));
            var deletion = storage.DeletionForId(id);
            _unitOfWork.Add(deletion);

            storage.Delete(IdentityMap, id);
        }

        public void Delete<T>(long id)
        {
            delete<T>(id);
        }

        public void Delete<T>(Guid id)
        {
            delete<T>(id);
        }

        public void Delete<T>(string id)
        {
            delete<T>(id);
        }

        public void DeleteWhere<T>(Expression<Func<T, bool>> expression)
        {
            assertNotDisposed();

            var model = Query<T>().Where(expression).As<MartenQueryable<T>>().ToQueryModel();

            var where = _schema.BuildWhereFragment(model);

            var deletion = _schema.StorageFor(typeof(T)).DeletionForWhere(where);

            _unitOfWork.Add(deletion);
        }

        public void Store<T>(params T[] entities)
        {
            assertNotDisposed();

            if (entities == null) throw new ArgumentNullException(nameof(entities));

            if (typeof(T).IsGenericEnumerable())
            {
                throw new ArgumentOutOfRangeException(typeof(T).Name, "Do not use IEnumerable<T> here as the document type. You may need to cast entities to an array instead.");
            }

            if (typeof(T) == typeof(object))
            {
                StoreObjects(entities.OfType<object>());
            }
            else
            {
                var storage = _schema.StorageFor(typeof(T));
                var idAssignment = _schema.IdAssignmentFor<T>();

                foreach (var entity in entities)
                {
                    if (_unitOfWork.Contains<T>(entity)) continue;

                    var assigned = false;
                    var id = idAssignment.Assign(entity, out assigned);

                    storage.Store(IdentityMap, id, entity);
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

        public void Store<T>(T entity, Guid version)
        {
            assertNotDisposed();

            var storage = _schema.StorageFor(typeof(T));
            var id = storage.Identity(entity);

            IdentityMap.Versions.Store<T>(id, version);

            Store(entity);
        }

        public IUnitOfWork PendingChanges => _unitOfWork;

        public void StoreObjects(IEnumerable<object> documents)
        {
            assertNotDisposed();

            documents.Where(x => x != null).GroupBy(x => x.GetType()).Each(group =>
            {
                var handler = typeof(Handler<>).CloseAndBuildAs<IHandler>(group.Key);
                handler.Store(this, group);
            });
        }

        public IEventStore Events { get; }

        public void SaveChanges()
        {
            if (!_unitOfWork.HasAnyUpdates()) return;

            assertNotDisposed();

            _connection.BeginTransaction();

            applyProjections();

            _options.Listeners.Each(x => x.BeforeSaveChanges(this));

            var batch = new UpdateBatch(_options, _serializer, _connection, IdentityMap.Versions, WriterPool);
            var changes = _unitOfWork.ApplyChanges(batch);

            try
            {
                _connection.Commit();
            }           
            catch (Exception)
            {
                // This code has a try/catch in it to stop
                // any errors from propogating from the rollback
                _connection.Rollback();
                
                throw;
            }

            Logger.RecordSavedChanges(this, changes);

            _options.Listeners.Each(x => x.AfterCommit(this, changes));
        }

        public async Task SaveChangesAsync(CancellationToken token)
        {
            if (!_unitOfWork.HasAnyUpdates()) return;

            assertNotDisposed();

            await _connection.BeginTransactionAsync(token).ConfigureAwait(false);

            await applyProjectionsAsync(token).ConfigureAwait(false);

            foreach (var listener in _options.Listeners)
            {
                await listener.BeforeSaveChangesAsync(this, token).ConfigureAwait(false);
            }

            var batch = new UpdateBatch(_options, _serializer, _connection, IdentityMap.Versions, WriterPool);
            var changes = await _unitOfWork.ApplyChangesAsync(batch, token).ConfigureAwait(false);


            try
            {
                await _connection.CommitAsync(token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // This code has a try/catch in it to stop
                // any errors from propogating from the rollback
                await _connection.RollbackAsync(token).ConfigureAwait(false);

                throw;
            }

            Logger.RecordSavedChanges(this, changes);

            foreach (var listener in _options.Listeners)
            {
                await listener.AfterCommitAsync(this, changes, token).ConfigureAwait(false);
            }
        }

        public IPatchExpression<T> Patch<T>(int id)
        {
            return patchById<T>(id);
        }

        public IPatchExpression<T> Patch<T>(long id)
        {
            return patchById<T>(id);
        }

        public IPatchExpression<T> Patch<T>(string id)
        {
            return patchById<T>(id);
        }

        public IPatchExpression<T> Patch<T>(Guid id)
        {
            return patchById<T>(id);
        }

        private IPatchExpression<T> patchById<T>(object id)
        {
            assertNotDisposed();

            var @where = new WhereFragment("where d.id = ?", id);
            return new PatchExpression<T>(@where, _schema, _unitOfWork);
        }

        public IPatchExpression<T> Patch<T>(Expression<Func<T, bool>> @where)
        {
            assertNotDisposed();

            var model = Query<T>().Where(@where).As<MartenQueryable<T>>().ToQueryModel();

            var fragment = _schema.BuildWhereFragment(model);

            return new PatchExpression<T>(fragment, _schema, _unitOfWork);
        }

        public IPatchExpression<T> Patch<T>(IWhereFragment fragment)
        {
            assertNotDisposed();

            return new PatchExpression<T>(fragment, _schema, _unitOfWork);
        }

        public void QueueOperation(IStorageOperation storageOperation)
        {
            assertNotDisposed();
            _unitOfWork.Add(storageOperation);
        }
        
        private void applyProjections()
        {
            var streams = PendingChanges.Streams().ToArray();
            foreach (var projection in _schema.Events.InlineProjections)
            {
                projection.Apply(this, streams);
            }
        }

        private async Task applyProjectionsAsync(CancellationToken token)
        {
            var streams = PendingChanges.Streams().ToArray();
            foreach (var projection in _schema.Events.InlineProjections)
            {
                await projection.ApplyAsync(this, streams, token);
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
}