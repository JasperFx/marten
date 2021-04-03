using System;
using System.Linq.Expressions;
using Marten.Exceptions;
using Marten.Internal.Storage;
using Marten.Linq.SqlGeneration;

#nullable enable
namespace Marten.Internal.Sessions
{
    public abstract partial class DocumentSessionBase
    {
        public void Delete<T>(T entity)
        {
            assertNotDisposed();
            var documentStorage = StorageFor<T>();

            var deletion = documentStorage.DeleteForDocument(entity, Tenant);
            _workTracker.Add(deletion);

            documentStorage.Eject(this, entity);
        }

        public void Delete<T>(int id)
        {
            assertNotDisposed();

            var storage = StorageFor<T>();

            if (storage is IDocumentStorage<T, int> i)
            {
                _workTracker.Add(i.DeleteForId(id, Tenant));

                ejectById<T>(id);
            }
            else if (storage is IDocumentStorage<T, long> l)
            {
                _workTracker.Add(l.DeleteForId(id, Tenant));

                ejectById<T>((long)id);
            }
            else
            {
                throw new DocumentIdTypeMismatchException(storage, typeof(int));
            }
        }

        public void Delete<T>(long id)
        {
            assertNotDisposed();
            var deletion = StorageFor<T, long>().DeleteForId(id, Tenant);
            _workTracker.Add(deletion);

            ejectById<T>(id);
        }

        public void Delete<T>(Guid id)
        {
            assertNotDisposed();
            var deletion = StorageFor<T, Guid>().DeleteForId(id, Tenant);
            _workTracker.Add(deletion);

            ejectById<T>(id);
        }

        public void Delete<T>(string id)
        {
            assertNotDisposed();

            var deletion = StorageFor<T, string>().DeleteForId(id, Tenant);
            _workTracker.Add(deletion);

            ejectById<T>(id);
        }

        public void DeleteWhere<T>(Expression<Func<T, bool>> expression)
        {
            assertNotDisposed();

            var documentStorage = StorageFor<T>();
            var deletion = new Deletion(documentStorage, documentStorage.DeleteFragment);
            deletion.ApplyFiltering(this, expression);

            _workTracker.Add(deletion);
        }

    }
}
