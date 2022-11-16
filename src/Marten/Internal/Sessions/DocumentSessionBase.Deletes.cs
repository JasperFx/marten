using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Marten.Exceptions;
using Marten.Internal.Storage;
using Marten.Linq.SqlGeneration;

#nullable enable
namespace Marten.Internal.Sessions
{
    public abstract partial class DocumentSessionBase
    {
        public void Delete<T>(T entity) where T: notnull
        {
            assertNotDisposed();
            var documentStorage = StorageFor<T>();

            var deletion = documentStorage.DeleteForDocument(entity, TenantId);
            _workTracker.Add(deletion);

            documentStorage.Eject(this, entity);
        }

        public void Delete<T>(int id) where T : notnull
        {
            assertNotDisposed();

            var storage = StorageFor<T>();

            if (storage is IDocumentStorage<T, int> i)
            {
                _workTracker.Add(i.DeleteForId(id, TenantId));

                ejectById<T>(id);
            }
            else if (storage is IDocumentStorage<T, long> l)
            {
                _workTracker.Add(l.DeleteForId(id, TenantId));

                ejectById<T>((long)id);
            }
            else
            {
                throw new DocumentIdTypeMismatchException(storage, typeof(int));
            }
        }

        public void Delete<T>(long id) where T : notnull
        {
            assertNotDisposed();
            var deletion = StorageFor<T, long>().DeleteForId(id, TenantId);
            _workTracker.Add(deletion);

            ejectById<T>(id);
        }

        public void Delete<T>(Guid id) where T : notnull
        {
            assertNotDisposed();
            var deletion = StorageFor<T, Guid>().DeleteForId(id, TenantId);
            _workTracker.Add(deletion);

            ejectById<T>(id);
        }

        public void Delete<T>(string id) where T : notnull
        {
            assertNotDisposed();

            var deletion = StorageFor<T, string>().DeleteForId(id, TenantId);
            _workTracker.Add(deletion);

            ejectById<T>(id);
        }

        public void DeleteWhere<T>(Expression<Func<T, bool>> expression) where T : notnull
        {
            assertNotDisposed();

            var documentStorage = StorageFor<T>();
            var deletion = new Deletion(documentStorage, documentStorage.DeleteFragment, TenantId);
            deletion.ApplyFiltering(this, expression);

            _workTracker.Add(deletion);
        }

        public void DeleteObjects(IEnumerable<object> documents)
        {
            assertNotDisposed();

            var documentsGroupedByType = documents
                .Where(x => x != null)
                .GroupBy(x => x.GetType());

            foreach (var group in documentsGroupedByType)
            {
                var handler = typeof(DeleteHandler<>).CloseAndBuildAs<IObjectHandler>(group.Key);
                handler.Execute(this, group);
            }
        }
    }
}
