#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Internal.Storage;
using Marten.Linq.QueryHandlers;

namespace Marten.Internal.Sessions;

public partial class QuerySession
{
    public async Task<bool> CheckExistsAsync<T>(string id, CancellationToken token = default) where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T), token).ConfigureAwait(false);
        var storage = StorageFor<T, string>();
        var handler = new CheckExistsByIdHandler<T, string>(storage, id);
        return await ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
    }

    public async Task<bool> CheckExistsAsync<T>(int id, CancellationToken token = default) where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T), token).ConfigureAwait(false);
        var storage = StorageFor<T>();

        if (storage is IDocumentStorage<T, int> i)
        {
            var handler = new CheckExistsByIdHandler<T, int>(i, id);
            return await ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
        }

        if (storage is IDocumentStorage<T, long> l)
        {
            var handler = new CheckExistsByIdHandler<T, long>(l, id);
            return await ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
        }

        throw new DocumentIdTypeMismatchException(
            $"The identity type for document type {typeof(T).FullNameInCode()} is not numeric");
    }

    public async Task<bool> CheckExistsAsync<T>(long id, CancellationToken token = default) where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T), token).ConfigureAwait(false);
        var storage = StorageFor<T, long>();
        var handler = new CheckExistsByIdHandler<T, long>(storage, id);
        return await ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
    }

    public async Task<bool> CheckExistsAsync<T>(Guid id, CancellationToken token = default) where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T), token).ConfigureAwait(false);
        var storage = StorageFor<T, Guid>();
        var handler = new CheckExistsByIdHandler<T, Guid>(storage, id);
        return await ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
    }

    public async Task<bool> CheckExistsAsync<T>(object id, CancellationToken token = default) where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T), token).ConfigureAwait(false);
        var loader = typeof(ExistsChecker<>).CloseAndBuildAs<IExistsChecker>(id.GetType());
        return await loader.CheckExistsAsync<T>(id, this, token).ConfigureAwait(false);
    }

    private interface IExistsChecker
    {
        Task<bool> CheckExistsAsync<T>(object id, QuerySession session, CancellationToken token = default) where T : notnull;
    }

    private class ExistsChecker<TId>: IExistsChecker where TId : notnull
    {
        public async Task<bool> CheckExistsAsync<T>(object id, QuerySession session, CancellationToken token = default) where T : notnull
        {
            var storage = session.StorageFor<T, TId>();
            var handler = new CheckExistsByIdHandler<T, TId>(storage, (TId)id);
            return await session.ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
        }
    }
}
