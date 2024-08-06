#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Internal.Storage;
using Marten.Linq.SqlGeneration;

namespace Marten.Internal.Sessions;

public abstract partial class DocumentSessionBase
{
    public void Delete<T>(T entity) where T : notnull
    {
        assertNotDisposed();
        var documentStorage = StorageFor<T>();

        if (documentStorage is IDocumentStorage<T, Guid> g) _workTracker.PurgeOperations<T, Guid>(g.Identity(entity));
        if (documentStorage is IDocumentStorage<T, string> s) _workTracker.PurgeOperations<T, string>(s.Identity(entity));
        if (documentStorage is IDocumentStorage<T, int> i) _workTracker.PurgeOperations<T, int>(i.Identity(entity));
        if (documentStorage is IDocumentStorage<T, long> l) _workTracker.PurgeOperations<T, long>(l.Identity(entity));

        var deletion = documentStorage.DeleteForDocument(entity, TenantId);
        _workTracker.Add(deletion);

        documentStorage.Eject(this, entity);
        ChangeTrackers.RemoveAll(x => ReferenceEquals(entity, x.Document));
    }

    public void Delete<T>(int id) where T : notnull
    {
        assertNotDisposed();

        var storage = StorageFor<T>();

        if (storage is IDocumentStorage<T, int> i)
        {
            _workTracker.Add(i.DeleteForId(id, TenantId));

            _workTracker.PurgeOperations<T, int>(id);
            ejectById<T>(id);
        }
        else if (storage is IDocumentStorage<T, long> l)
        {
            _workTracker.Add(l.DeleteForId(id, TenantId));

            _workTracker.PurgeOperations<T, long>(id);
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

        _workTracker.PurgeOperations<T, long>(id);
        ejectById<T>(id);
    }

    public void Delete<T>(object id) where T : notnull
    {
        var deleter = typeof(ByIdDeleter<>).CloseAndBuildAs<IByIdDeleter>(this, id.GetType());
        deleter.Delete<T>(id);
    }

    private interface IByIdDeleter
    {
        void Delete<T>(object id) where T : notnull;
    }

    private class ByIdDeleter<TId>: IByIdDeleter
    {
        private readonly DocumentSessionBase _parent;

        public ByIdDeleter(DocumentSessionBase parent)
        {
            _parent = parent;
        }

        public void Delete<T>(object id) where T : notnull
        {
            var typed = (TId)id;

            _parent.assertNotDisposed();
            var documentStorage = _parent.StorageFor<T, TId>();
            var deletion = documentStorage.DeleteForId(typed, _parent.TenantId);
            _parent._workTracker.PurgeOperations<T, TId>(typed);
            _parent._workTracker.Add(deletion);

            switch (_parent.TrackingMode)
            {
                case DocumentTracking.IdentityOnly:
                    documentStorage.EjectById(_parent, typed);
                    break;

                case DocumentTracking.DirtyTracking:
                    documentStorage.EjectById(_parent, typed);
                    documentStorage.RemoveDirtyTracker(_parent, id);
                    break;
            }
        }
    }

    public void Delete<T>(Guid id) where T : notnull
    {
        assertNotDisposed();
        var deletion = StorageFor<T, Guid>().DeleteForId(id, TenantId);
        _workTracker.PurgeOperations<T, Guid>(id);
        _workTracker.Add(deletion);

        ejectById<T>(id);
    }

    public void Delete<T>(string id) where T : notnull
    {
        assertNotDisposed();

        var deletion = StorageFor<T, string>().DeleteForId(id, TenantId);
        _workTracker.Add(deletion);

        _workTracker.PurgeOperations<T, string>(id);
        ejectById<T>(id);
    }

    public void DeleteWhere<T>(Expression<Func<T, bool>> expression) where T : notnull
    {
        assertNotDisposed();

        var documentStorage = StorageFor<T>();
        var deletion = new Deletion(documentStorage, documentStorage.DeleteFragment);
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
