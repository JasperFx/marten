using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Events;
using Marten.Events.Projections.Async;
using Marten.Linq;
using Marten.Patching;
using Marten.Services;
using Marten.Storage;
using Remotion.Linq.Parsing.Structure;

namespace Marten
{
    public class DocumentSession : QuerySession, IDocumentSession
    {
        private readonly IManagedConnection _connection;
        private readonly UnitOfWork _unitOfWork;

        public DocumentSession(DocumentStore store, IManagedConnection connection, IQueryParser parser, IIdentityMap identityMap, ITenant tenant, ConcurrencyChecks concurrency, IList<IDocumentSessionListener> localListeners)
            : base(store, connection, parser, identityMap, tenant)

        {
            _connection = connection;
            Listeners = _store.Options.Listeners.Concat(localListeners).ToList();

            IdentityMap = identityMap;
            Concurrency = concurrency;

            _unitOfWork = new UnitOfWork(_store, tenant);

            if (IdentityMap is IDocumentTracker)
            {
                _unitOfWork.AddTracker(IdentityMap.As<IDocumentTracker>());
            }

            Events = new EventStore(this, _store, _connection, _unitOfWork, tenant);
        }

        public IList<IDocumentSessionListener> Listeners { get; }

        // This is here for testing purposes, not part of IDocumentSession
        public IIdentityMap IdentityMap { get; }

        /// <summary>
        /// Use to enable or disable optimistic concurrency checks for this session
        /// </summary>
        public ConcurrencyChecks Concurrency { get; set; }

        public void Delete<T>(T entity)
        {
            assertNotDisposed();

            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var storage = Tenant.StorageFor(typeof(T));
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
            var storage = Tenant.StorageFor(typeof(T));
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

            var where = QueryModelExtensions.BuildWhereFragment(_store, model, Tenant);

            var deletion = Tenant.StorageFor(typeof(T)).DeletionForWhere(@where);

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
                var storage = Tenant.StorageFor(typeof(T));
                var idAssignment = Tenant.IdAssignmentFor<T>();

                foreach (var entity in entities)
                {
                    if (_unitOfWork.Contains<T>(entity)) continue;

                    var assigned = false;
                    var id = idAssignment.Assign(Tenant, entity, out assigned);

                    storage.Store(IdentityMap, id, entity);
                    if (assigned)
                    {
                        _unitOfWork.StoreInserts(entity);
                    }
                    else
                    {
                        _unitOfWork.StoreUpserts(entity);
                    }
                }
            }
        }

        public void Store<T>(string tenantId, params T[] entities)
        {
            assertNotDisposed();

            if (entities == null) throw new ArgumentNullException(nameof(entities));

            if (typeof(T).IsGenericEnumerable())
            {
                throw new ArgumentOutOfRangeException(typeof(T).Name, "Do not use IEnumerable<T> here as the document type. You may need to cast entities to an array instead.");
            }

            if (typeof(T) == typeof(object))
            {
                throw new ArgumentOutOfRangeException("T","'object' is not supported here");
            }
            else
            {
                var storage = Tenant.StorageFor(typeof(T));
                var idAssignment = Tenant.IdAssignmentFor<T>();

                foreach (var entity in entities)
                {
                    if (_unitOfWork.Contains<T>(entity)) continue;

                    var assigned = false;
                    var id = idAssignment.Assign(Tenant, entity, out assigned);

                    storage.Store(IdentityMap, id, entity);
                    _unitOfWork.Add(new UpsertDocument(entity, tenantId));
                }
            }
        }

        public void Insert<T>(params T[] entities)
        {
            assertNotDisposed();

            if (entities == null) throw new ArgumentNullException(nameof(entities));

            if (typeof(T).IsGenericEnumerable())
            {
                throw new ArgumentOutOfRangeException(typeof(T).Name, "Do not use IEnumerable<T> here as the document type. You may need to cast entities to an array instead.");
            }

            if (typeof(T) == typeof(object))
            {
                InsertObjects(entities.OfType<object>());
            }
            else
            {
                var storage = Tenant.StorageFor(typeof(T));
                var idAssignment = Tenant.IdAssignmentFor<T>();

                foreach (var entity in entities)
                {
                    if (_unitOfWork.Contains<T>(entity)) continue;

                    var assigned = false;
                    var id = idAssignment.Assign(Tenant, entity, out assigned);

                    storage.Store(IdentityMap, id, entity);
                    _unitOfWork.StoreInserts(entity);
                }
            }
        }

        public void Update<T>(params T[] entities)
        {
            assertNotDisposed();

            if (entities == null) throw new ArgumentNullException(nameof(entities));

            if (typeof(T).IsGenericEnumerable())
            {
                throw new ArgumentOutOfRangeException(typeof(T).Name, "Do not use IEnumerable<T> here as the document type. You may need to cast entities to an array instead.");
            }

            if (typeof(T) == typeof(object))
            {
                InsertObjects(entities.OfType<object>());
            }
            else
            {
                var storage = Tenant.StorageFor(typeof(T));
                var idAssignment = Tenant.IdAssignmentFor<T>();

                foreach (var entity in entities)
                {
                    if (_unitOfWork.Contains<T>(entity)) continue;

                    var assigned = false;
                    var id = idAssignment.Assign(Tenant, entity, out assigned);

                    storage.Store(IdentityMap, id, entity);
                    _unitOfWork.StoreUpdates(entity);
                }
            }
        }


        public void Store<T>(T entity, Guid version)
        {
            assertNotDisposed();

            var storage = Tenant.StorageFor(typeof(T));
            var id = storage.Identity(entity);

            // This is important for subclassing to use the document type
            // from the storage rather than the T that might be a
            // subclass type
            IdentityMap.Versions.Store(storage.TopLevelBaseType, id, version);

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

        public void InsertObjects(IEnumerable<object> documents)
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

            foreach (var listener in Listeners)
            {
                listener.BeforeSaveChanges(this);
            }
            
            var batch = new UpdateBatch(_store, _connection, IdentityMap.Versions, WriterPool, Tenant, Concurrency);
            var changes = _unitOfWork.ApplyChanges(batch);
            EjectPatchedTypes(changes);

            try
            {
                _connection.Commit();                
                IdentityMap.ClearChanges();
            }           
            catch (Exception)
            {
                // This code has a try/catch in it to stop
                // any errors from propogating from the rollback
                _connection.Rollback();
                
                throw;
            }

            Logger.RecordSavedChanges(this, changes);

            foreach (var listener in Listeners)
            {
                listener.AfterCommit(this, changes);
            }
        }

        public async Task SaveChangesAsync(CancellationToken token)
        {
            if (!_unitOfWork.HasAnyUpdates()) return;

            assertNotDisposed();

            await _connection.BeginTransactionAsync(token).ConfigureAwait(false);

            await applyProjectionsAsync(token).ConfigureAwait(false);

            foreach (var listener in Listeners)
            {
                await listener.BeforeSaveChangesAsync(this, token).ConfigureAwait(false);
            }

            var batch = new UpdateBatch(_store, _connection, IdentityMap.Versions, WriterPool, Tenant, Concurrency);
            var changes = await _unitOfWork.ApplyChangesAsync(batch, token).ConfigureAwait(false);
            EjectPatchedTypes(changes);

            try
            {
                await _connection.CommitAsync(token).ConfigureAwait(false);
                IdentityMap.ClearChanges();                
            }
            catch (Exception)
            {
                // This code has a try/catch in it to stop
                // any errors from propogating from the rollback
                await _connection.RollbackAsync(token).ConfigureAwait(false);

                throw;
            }

            Logger.RecordSavedChanges(this, changes);

            foreach (var listener in Listeners)
            {
                await listener.AfterCommitAsync(this, changes, token).ConfigureAwait(false);
            }
        }

        private void EjectPatchedTypes(ChangeSet changes)
        {
            var patchedTypes = changes.Operations.OfType<PatchOperation>().Select(x => x.DocumentType).Distinct().ToArray();
            foreach (var type in patchedTypes)
            {
                EjectAllOfType(type);
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

            var @where = new WhereFragment("d.id = ?", id);
            return new PatchExpression<T>(@where, Tenant, _unitOfWork, _store.Serializer);
        }

        public IPatchExpression<T> Patch<T>(Expression<Func<T, bool>> @where)
        {
            assertNotDisposed();

            var model = Query<T>().Where(@where).As<MartenQueryable<T>>().ToQueryModel();

            var fragment = QueryModelExtensions.BuildWhereFragment(_store, model, Tenant);

            return new PatchExpression<T>(fragment, Tenant, _unitOfWork, _store.Serializer);
        }

        public IPatchExpression<T> Patch<T>(IWhereFragment fragment)
        {
            assertNotDisposed();

            return new PatchExpression<T>(fragment, Tenant, _unitOfWork, _store.Serializer);
        }

        public void QueueOperation(IStorageOperation storageOperation)
        {
            assertNotDisposed();
            _unitOfWork.Add(storageOperation);
        }

        public void Eject<T>(T document)
        {
            var id = Tenant.StorageFor<T>().Identity(document);
            IdentityMap.Remove<T>(id);

            _unitOfWork.Eject(document);            
        }

        public void EjectAllOfType(Type type)
        {
            IdentityMap.RemoveAllOfType(type);
        }

        private void applyProjections()
        {
            var eventPage = new EventPage(PendingChanges.Streams().ToArray());
            foreach (var projection in _store.Events.InlineProjections)
            {
                projection.Apply(this, eventPage);
            }
        }

        private async Task applyProjectionsAsync(CancellationToken token)
        {
            var eventPage = new EventPage(PendingChanges.Streams().ToArray());
            foreach (var projection in _store.Events.InlineProjections)
            {
                await projection.ApplyAsync(this, eventPage, token).ConfigureAwait(false);
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

        internal class InsertHandler<T> : IHandler
        {
            public void Store(IDocumentSession session, IEnumerable<object> objects)
            {
                session.Insert(objects.OfType<T>().ToArray());
            }
        }
    }
}