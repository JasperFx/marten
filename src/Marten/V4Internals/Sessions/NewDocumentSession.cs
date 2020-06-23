using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Linq;
using Marten.Patching;
using Marten.Services;

namespace Marten.V4Internals.Sessions
{
    public abstract class NewDocumentSession: QuerySession, IDocumentSession
    {
        private readonly IList<IStorageOperation> _pendingOperations = new List<IStorageOperation>();


        protected NewDocumentSession(IDocumentStore store, IDatabase database, ISerializer serializer, ITenant tenant, IPersistenceGraph persistence, StoreOptions options) : base(store, database, serializer, tenant, persistence, options)
        {
        }


        public void Delete<T>(T entity)
        {
            throw new NotImplementedException();
        }

        public void Delete<T>(int id)
        {
            throw new NotImplementedException();
        }

        public void Delete<T>(long id)
        {
            throw new NotImplementedException();
        }

        public void Delete<T>(Guid id)
        {
            throw new NotImplementedException();
        }

        public void Delete<T>(string id)
        {
            throw new NotImplementedException();
        }

        public void DeleteWhere<T>(Expression<Func<T, bool>> expression)
        {
            throw new NotImplementedException();
        }

        public void SaveChanges()
        {
            this.RunOperations(_pendingOperations);
        }

        public Task SaveChangesAsync(CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        public void Store<T>(IEnumerable<T> entities)
        {
            throw new NotImplementedException();
        }

        public void Store<T>(params T[] entities)
        {
            var storage = storageFor<T>();
            foreach (var entity in entities)
            {
                storage.Store(this, entity);
                var op = storage.Upsert(entity, this);
                _pendingOperations.Add(op);
            }
        }

        public void Store<T>(string tenantId, IEnumerable<T> entities)
        {
            throw new NotImplementedException();
        }

        public void Store<T>(string tenantId, params T[] entities)
        {
            throw new NotImplementedException();
        }

        public void Store<T>(T entity, Guid version)
        {
            throw new NotImplementedException();
        }

        public void Insert<T>(IEnumerable<T> entities)
        {
            throw new NotImplementedException();
        }

        public void Insert<T>(params T[] entities)
        {
            throw new NotImplementedException();
        }

        public void Update<T>(IEnumerable<T> entities)
        {
            throw new NotImplementedException();
        }

        public void Update<T>(params T[] entities)
        {
            throw new NotImplementedException();
        }

        public void InsertObjects(IEnumerable<object> documents)
        {
            throw new NotImplementedException();
        }

        public IUnitOfWork PendingChanges { get; }
        public void StoreObjects(IEnumerable<object> documents)
        {
            throw new NotImplementedException();
        }

        public IEventStore Events { get; }
        public ConcurrencyChecks Concurrency { get; }
        public IList<IDocumentSessionListener> Listeners { get; }
        public IPatchExpression<T> Patch<T>(int id)
        {
            throw new NotImplementedException();
        }

        public IPatchExpression<T> Patch<T>(long id)
        {
            throw new NotImplementedException();
        }

        public IPatchExpression<T> Patch<T>(string id)
        {
            throw new NotImplementedException();
        }

        public IPatchExpression<T> Patch<T>(Guid id)
        {
            throw new NotImplementedException();
        }

        public IPatchExpression<T> Patch<T>(Expression<Func<T, bool>> @where)
        {
            throw new NotImplementedException();
        }

        public IPatchExpression<T> Patch<T>(IWhereFragment fragment)
        {
            throw new NotImplementedException();
        }

        public void QueueOperation(Services.IStorageOperation storageOperation)
        {
            throw new NotImplementedException();
        }

        public void Eject<T>(T document)
        {
            throw new NotImplementedException();
        }

        public void EjectAllOfType(Type type)
        {
            throw new NotImplementedException();
        }
    }
}
