#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.CodeGeneration;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Internal.Storage;

namespace Marten.Internal.Sessions;

public partial class QuerySession
{
    public async Task<T?> LoadAsync<T>(string id, CancellationToken token = default) where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T), token).ConfigureAwait(false);
        var document = await StorageFor<T, string>().LoadAsync(id, this, token).ConfigureAwait(false);

        return document;
    }

    public async Task<T?> LoadAsync<T>(object id, CancellationToken token = default) where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T), token).ConfigureAwait(false);
        var loader = typeof(Loader<>).CloseAndBuildAs<ILoader>(id.GetType());
        return await loader.LoadAsync<T>(id, this, token).ConfigureAwait(false);
    }

    private interface ILoader
    {
        Task<T?> LoadAsync<T>(object id, QuerySession session, CancellationToken token = default) where T : notnull;
    }

    private class Loader<TId>: ILoader
    {
        public async Task<T?> LoadAsync<T>(object id, QuerySession session, CancellationToken token = default) where T : notnull
        {
            var document = await session.StorageFor<T, TId>().LoadAsync((TId)id, session, token).ConfigureAwait(false);

            return document;
        }
    }

    public async Task<T?> LoadAsync<T>(int id, CancellationToken token = default) where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T), token).ConfigureAwait(false);
        var storage = StorageFor<T>();

        var document = storage switch
        {
            IDocumentStorage<T, int> i => await i.LoadAsync(id, this, token).ConfigureAwait(false),
            IDocumentStorage<T, long> l => await l.LoadAsync(id, this, token).ConfigureAwait(false),
            _ => throw new DocumentIdTypeMismatchException(
                $"The identity type for document type {typeof(T).FullNameInCode()} is not numeric")
        };

        return document;
    }

    public async Task<T?> LoadAsync<T>(long id, CancellationToken token = default) where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T), token).ConfigureAwait(false);
        var document = await StorageFor<T, long>().LoadAsync(id, this, token).ConfigureAwait(false);

        return document;
    }

    public async Task<T?> LoadAsync<T>(Guid id, CancellationToken token = default) where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T), token).ConfigureAwait(false);
        var document = await StorageFor<T, Guid>().LoadAsync(id, this, token).ConfigureAwait(false);

        return document;
    }

    public async Task<IReadOnlyList<T>> LoadManyAsync<T>(params string[] ids) where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T)).ConfigureAwait(false);
        var documentStorage = StorageFor<T, string>();
        return await documentStorage.LoadManyAsync(ids, this, default).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<T>> LoadManyAsync<T>(IEnumerable<string> ids) where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T)).ConfigureAwait(false);
        var documentStorage = StorageFor<T, string>();
        return await documentStorage.LoadManyAsync(ids.ToArray(), this, default).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, params string[] ids) where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T), token).ConfigureAwait(false);
        var documentStorage = StorageFor<T, string>();
        return await documentStorage.LoadManyAsync(ids, this, token).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, IEnumerable<string> ids)
        where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T), token).ConfigureAwait(false);
        var documentStorage = StorageFor<T, string>();
        return await documentStorage.LoadManyAsync(ids.ToArray(), this, token).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<T>> LoadManyAsync<T>(params int[] ids) where T : notnull
    {
        return LoadManyAsync<T>(CancellationToken.None, ids);
    }

    public Task<IReadOnlyList<T>> LoadManyAsync<T>(IEnumerable<int> ids) where T : notnull
    {
        return LoadManyAsync<T>(ids.ToArray());
    }

    public async Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, params int[] ids) where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T), token).ConfigureAwait(false);

        var storage = StorageFor<T>();
        if (storage is IDocumentStorage<T, int> i)
        {
            return await i.LoadManyAsync(ids, this, token).ConfigureAwait(false);
        }

        if (storage is IDocumentStorage<T, long> l)
        {
            return await l.LoadManyAsync(ids.Select(x => (long)x).ToArray(), this, token).ConfigureAwait(false);
        }


        throw new DocumentIdTypeMismatchException(
            $"The identity type for document type {typeof(T).FullNameInCode()} is not numeric");
    }

    public Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, IEnumerable<int> ids) where T : notnull
    {
        return LoadManyAsync<T>(token, ids.ToArray());
    }

    public async Task<IReadOnlyList<T>> LoadManyAsync<T>(params long[] ids) where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T)).ConfigureAwait(false);
        var documentStorage = StorageFor<T, long>();
        return await documentStorage.LoadManyAsync(ids, this, default).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<T>> LoadManyAsync<T>(IEnumerable<long> ids) where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T)).ConfigureAwait(false);
        var documentStorage = StorageFor<T, long>();
        return await documentStorage.LoadManyAsync(ids.ToArray(), this, default).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, params long[] ids) where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T), token).ConfigureAwait(false);
        var documentStorage = StorageFor<T, long>();
        return await documentStorage.LoadManyAsync(ids, this, token).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, IEnumerable<long> ids)
        where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T), token).ConfigureAwait(false);
        var documentStorage = StorageFor<T, long>();
        return await documentStorage.LoadManyAsync(ids.ToArray(), this, token).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<T>> LoadManyAsync<T>(params Guid[] ids) where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T)).ConfigureAwait(false);
        var documentStorage = StorageFor<T, Guid>();
        return await documentStorage.LoadManyAsync(ids, this, default).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<T>> LoadManyAsync<T>(IEnumerable<Guid> ids) where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T)).ConfigureAwait(false);
        return await StorageFor<T, Guid>().LoadManyAsync(ids.ToArray(), this, default).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, params Guid[] ids) where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T), token).ConfigureAwait(false);
        return await StorageFor<T, Guid>().LoadManyAsync(ids, this, token).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, IEnumerable<Guid> ids)
        where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T), token).ConfigureAwait(false);
        return await StorageFor<T, Guid>().LoadManyAsync(ids.ToArray(), this, token).ConfigureAwait(false);
    }
}
