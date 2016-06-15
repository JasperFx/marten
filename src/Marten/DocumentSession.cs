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
using Marten.Services.Deletes;
using Remotion.Linq.Parsing.Structure;

namespace Marten
{
    public class DocumentSession : QuerySession, IDocumentSession
    {
        private readonly IList<IChangeSet> _changes = new List<IChangeSet>();
        private readonly IManagedConnection _connection;
        private readonly StoreOptions _options;
        private readonly IDocumentSchema _schema;
        private readonly ISerializer _serializer;
        private readonly UnitOfWork _unitOfWork;

        public DocumentSession(IDocumentStore store, StoreOptions options, IDocumentSchema schema,
            ISerializer serializer, IManagedConnection connection, IQueryParser parser, IIdentityMap identityMap)
            : base(store, schema, serializer, connection, parser, identityMap)
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
            var model = Query<T>().Where(expression).As<MartenQueryable<T>>().ToQueryModel();

            var where = _schema.BuildWhereFragment(model);

            var mapping = _schema.MappingFor(typeof(T));

            var deletion = new DeleteWhere(typeof(T), mapping.Table, where);

            _unitOfWork.Add(deletion);
        }

        public void Store<T>(params T[] entities)
        {
            if (entities == null) throw new ArgumentNullException(nameof(entities));

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
            var storage = _schema.StorageFor(typeof(T));
            var id = storage.Identity(entity);

            IdentityMap.Versions.Store<T>(id, version);

            Store(entity);
        }

        public IUnitOfWork PendingChanges => _unitOfWork;

        public void StoreObjects(IEnumerable<object> documents)
        {
            documents.Where(x => x != null).GroupBy(x => x.GetType()).Each(group =>
            {
                var handler = typeof(Handler<>).CloseAndBuildAs<IHandler>(group.Key);
                handler.Store(this, group);
            });
        }

        public IEventStore Events { get; }

        public void SaveChanges()
        {
            applyProjections();

            _options.Listeners.Each(x => x.BeforeSaveChanges(this));

            var batch = new UpdateBatch(_options, _serializer, _connection, IdentityMap.Versions);
            var changes = _unitOfWork.ApplyChanges(batch);
            _changes.Add(changes);


            _connection.Commit();

            Logger.RecordSavedChanges(this);

            _options.Listeners.Each(x => x.AfterCommit(this));
        }

        public async Task SaveChangesAsync(CancellationToken token)
        {
            await applyProjectionsAsync(token).ConfigureAwait(false);

            foreach (var listener in _options.Listeners)
            {
                await listener.BeforeSaveChangesAsync(this, token).ConfigureAwait(false);
            }

            var batch = new UpdateBatch(_options, _serializer, _connection, IdentityMap.Versions);
            var changes = await _unitOfWork.ApplyChangesAsync(batch, token).ConfigureAwait(false);

            _changes.Add(changes);

            _connection.Commit();

            Logger.RecordSavedChanges(this);

            foreach (var listener in _options.Listeners)
            {
                await listener.AfterCommitAsync(this, token).ConfigureAwait(false);
            }
        }

        public IEnumerable<IChangeSet> Commits => _changes;
        public IChangeSet LastCommit => _changes.LastOrDefault();
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
            var @where = new WhereFragment("d.id = ?", id);
            return new PatchExpression<T>(@where, _schema, _unitOfWork);
        }

        public IPatchExpression<T> Patch<T>(Expression<Func<T, bool>> @where)
        {
            var model = Query<T>().Where(@where).As<MartenQueryable<T>>().ToQueryModel();

            var fragment = _schema.BuildWhereFragment(model);

            return new PatchExpression<T>(fragment, _schema, _unitOfWork);
        }

        private void applyProjections()
        {
            var projections = _schema.Events.As<IProjections>();

            projections.Inlines.Each(x => x.Apply(this, PendingChanges.Streams().ToArray()));
        }

        private async Task applyProjectionsAsync(CancellationToken token)
        {
            var projections = _schema.Events.As<IProjections>();

            foreach (var projection in projections.Inlines)
            {
                await projection.ApplyAsync(this, PendingChanges.Streams().ToArray(), token).ConfigureAwait(false);
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