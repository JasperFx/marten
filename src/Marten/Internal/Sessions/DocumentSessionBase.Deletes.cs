using System;
using System.Linq.Expressions;
using Marten.Exceptions;
using Marten.Internal.Storage;
using Marten.Linq.SqlGeneration;

namespace Marten.Internal.Sessions
{
    public abstract partial class DocumentSessionBase
    {
        public void Delete<T>(T entity)
        {
            assertNotDisposed();
            var documentStorage = StorageFor<T>();

            var deletion = documentStorage.DeleteForDocument(entity);
            _unitOfWork.Add(deletion);

            documentStorage.Eject(this, entity);
        }

        public void Delete<T>(int id)
        {
            assertNotDisposed();

            var storage = StorageFor<T>();

            if (storage is IDocumentStorage<T, int> i)
            {
                _unitOfWork.Add(i.DeleteForId(id));

                ejectById<T>(id);
            }
            else if (storage is IDocumentStorage<T, long> l)
            {
                _unitOfWork.Add(l.DeleteForId(id));

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
            var deletion = storageFor<T, long>().DeleteForId(id);
            _unitOfWork.Add(deletion);

            ejectById<T>(id);
        }

        public void Delete<T>(Guid id)
        {
            assertNotDisposed();
            var deletion = storageFor<T, Guid>().DeleteForId(id);
            _unitOfWork.Add(deletion);

            ejectById<T>(id);
        }

        public void Delete<T>(string id)
        {
            assertNotDisposed();

            var deletion = storageFor<T, string>().DeleteForId(id);
            _unitOfWork.Add(deletion);

            ejectById<T>(id);
        }

        public void DeleteInTenant<T>(string tenantId, T document)
        {
            assertNotDisposed();
            var tenant = Tenancy[tenantId];
            var documentStorage = selectStorage(tenant.Providers.StorageFor<T>());


            var deletion = documentStorage.DeleteForDocument(document, tenant);
            _unitOfWork.Add(deletion);

            documentStorage.Eject(this, document);
        }

        public void DeleteByIdInTenant<T>(string tenantId, Guid id)
        {
            assertNotDisposed();

            var tenant = Tenancy[tenantId];
            var storage = (IDocumentStorage<T, Guid>)selectStorage(tenant.Providers.StorageFor<T>());

            var deletion = storage.DeleteForId(id, tenant);
            _unitOfWork.Add(deletion);

            ejectById<T>(id);
        }

        public void DeleteByIdInTenant<T>(string tenantId, int id)
        {
            assertNotDisposed();

            var tenant = Tenancy[tenantId];
            var storage = selectStorage(tenant.Providers.StorageFor<T>());

            if (storage is IDocumentStorage<T, int> i)
            {
                _unitOfWork.Add(i.DeleteForId(id, tenant));

                ejectById<T>(id);
            }
            else if (storage is IDocumentStorage<T, long> l)
            {
                _unitOfWork.Add(l.DeleteForId(id, tenant));

                ejectById<T>((long)id);
            }
            else
            {
                throw new DocumentIdTypeMismatchException(storage, typeof(int));
            }
        }

        public void DeleteByIdInTenant<T>(string tenantId, string id)
        {
            assertNotDisposed();

            var tenant = Tenancy[tenantId];
            var storage = (IDocumentStorage<T, string>)selectStorage(tenant.Providers.StorageFor<T>());


            var deletion = storage.DeleteForId(id, tenant);
            _unitOfWork.Add(deletion);

            ejectById<T>(id);
        }

        public void DeleteByIdInTenant<T>(string tenantId, long id)
        {
            assertNotDisposed();

            var tenant = Tenancy[tenantId];
            var storage = (IDocumentStorage<T, long>)selectStorage(tenant.Providers.StorageFor<T>());


            var deletion = storage.DeleteForId(id, tenant);
            _unitOfWork.Add(deletion);

            ejectById<T>(id);
        }

        public void DeleteWhere<T>(Expression<Func<T, bool>> expression)
        {
            assertNotDisposed();

            var documentStorage = StorageFor<T>();
            var deletion = new Deletion(documentStorage, documentStorage.DeleteFragment);
            deletion.ApplyFiltering(this, expression);

            _unitOfWork.Add(deletion);
        }

    }
}
