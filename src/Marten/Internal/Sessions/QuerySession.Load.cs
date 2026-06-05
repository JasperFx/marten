#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Internal.Storage;

namespace Marten.Internal.Sessions;

[UnconditionalSuppressMessage("Trimming", "IL2067",
    Justification = "Class-level: parameter receives a DAM-annotated Type from a reflective lookup whose source type is preserved at the StoreOptions / projection-registration boundary.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
public partial class QuerySession
{
    /// <summary>
    /// Virtual chokepoint for the public <see cref="LoadAsync{T}(Guid, CancellationToken)"/>
    /// family. Default routes through <see cref="IDocumentStorage{T, TId}.LoadAsync"/>
    /// (session-aware: writes session-shared trackers per row).
    /// <see cref="Marten.Events.Daemon.Internals.ProjectionDocumentSession"/> overrides
    /// to dispatch through <see cref="IDocumentStorage{T, TId}.LoadProjectedAsync"/>
    /// instead so user-supplied <c>operations.LoadAsync&lt;X&gt;(...)</c> calls from
    /// inside an aggregation projection's EvolveAsync never touch
    /// <c>_session.Versions</c> / <c>_session.ItemMap</c> / <c>_session.ChangeTrackers</c>
    /// (#4667 Phase 3).
    /// </summary>
    /// <remarks>
    /// Uses <c>[return: MaybeNull]</c> + bare <c>T</c> rather than <c>T?</c>
    /// because the override on <see cref="DocumentSessionBase"/>'s descendants
    /// (a partial-class chain not all in nullable-enable context) loses the
    /// reference-type-vs-Nullable&lt;T&gt; disambiguation of <c>T?</c> at the
    /// override site; the attribute form is unambiguous either way.
    /// </remarks>
    [return: MaybeNull]
    protected internal virtual Task<T> ExecuteLoadOneAsync<T, TId>(IDocumentStorage<T, TId> storage, TId id, CancellationToken token)
        where T : notnull where TId : notnull
        => storage.LoadAsync(id, this, token)!;

    /// <summary>
    /// Virtual chokepoint for the public <see cref="LoadManyAsync{T}(Guid[])"/>
    /// family. See <see cref="ExecuteLoadOneAsync{T, TId}"/>.
    /// </summary>
    protected internal virtual Task<IReadOnlyList<T>> ExecuteLoadManyAsync<T, TId>(IDocumentStorage<T, TId> storage, TId[] ids, CancellationToken token)
        where T : notnull where TId : notnull
        => storage.LoadManyAsync(ids, this, token);

    public async Task<T?> LoadAsync<T>(string id, CancellationToken token = default) where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T), token).ConfigureAwait(false);
        var document = await ExecuteLoadOneAsync(StorageFor<T, string>(), id, token).ConfigureAwait(false);

        return document;
    }

    public async Task<T?> LoadAsync<T>(object id, CancellationToken token = default) where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T), token).ConfigureAwait(false);
        // 9.0 (#4373): replace per-call Activator.CreateInstance with delegate-cached
        // factory keyed on id.GetType(). One reflection pass on first-encountered
        // identity type; subsequent calls hit the cached factory delegate.
        var loader = GenericFactoryCache.BuildAs<ILoader>(
            typeof(Loader<>),
            id.GetType(),
            static closed => () => (ILoader)Activator.CreateInstance(closed)!);
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
            var document = await session.ExecuteLoadOneAsync(session.StorageFor<T, TId>(), (TId)id, token).ConfigureAwait(false);

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
            IDocumentStorage<T, int> i => await ExecuteLoadOneAsync(i, id, token).ConfigureAwait(false),
            IDocumentStorage<T, long> l => await ExecuteLoadOneAsync(l, (long)id, token).ConfigureAwait(false),
            _ => throw new DocumentIdTypeMismatchException(
                $"The identity type for document type {typeof(T).FullNameInCode()} is not numeric")
        };

        return document;
    }

    public async Task<T?> LoadAsync<T>(long id, CancellationToken token = default) where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T), token).ConfigureAwait(false);
        var document = await ExecuteLoadOneAsync(StorageFor<T, long>(), id, token).ConfigureAwait(false);

        return document;
    }

    public async Task<T?> LoadAsync<T>(Guid id, CancellationToken token = default) where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T), token).ConfigureAwait(false);
        var document = await ExecuteLoadOneAsync(StorageFor<T, Guid>(), id, token).ConfigureAwait(false);

        return document;
    }

    public async Task<IReadOnlyList<T>> LoadManyAsync<T>(params string[] ids) where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T)).ConfigureAwait(false);
        var documentStorage = StorageFor<T, string>();
        return await ExecuteLoadManyAsync(documentStorage, ids, default).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<T>> LoadManyAsync<T>(IEnumerable<string> ids) where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T)).ConfigureAwait(false);
        var documentStorage = StorageFor<T, string>();
        return await ExecuteLoadManyAsync(documentStorage, ids.ToArray(), default).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, params string[] ids) where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T), token).ConfigureAwait(false);
        var documentStorage = StorageFor<T, string>();
        return await ExecuteLoadManyAsync(documentStorage, ids, token).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, IEnumerable<string> ids)
        where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T), token).ConfigureAwait(false);
        var documentStorage = StorageFor<T, string>();
        return await ExecuteLoadManyAsync(documentStorage, ids.ToArray(), token).ConfigureAwait(false);
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
            return await ExecuteLoadManyAsync(i, ids, token).ConfigureAwait(false);
        }

        if (storage is IDocumentStorage<T, long> l)
        {
            return await ExecuteLoadManyAsync(l, ids.Select(x => (long)x).ToArray(), token).ConfigureAwait(false);
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
        return await ExecuteLoadManyAsync(documentStorage, ids, default).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<T>> LoadManyAsync<T>(IEnumerable<long> ids) where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T)).ConfigureAwait(false);
        var documentStorage = StorageFor<T, long>();
        return await ExecuteLoadManyAsync(documentStorage, ids.ToArray(), default).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, params long[] ids) where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T), token).ConfigureAwait(false);
        var documentStorage = StorageFor<T, long>();
        return await ExecuteLoadManyAsync(documentStorage, ids, token).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, IEnumerable<long> ids)
        where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T), token).ConfigureAwait(false);
        var documentStorage = StorageFor<T, long>();
        return await ExecuteLoadManyAsync(documentStorage, ids.ToArray(), token).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<T>> LoadManyAsync<T>(params Guid[] ids) where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T)).ConfigureAwait(false);
        var documentStorage = StorageFor<T, Guid>();
        return await ExecuteLoadManyAsync(documentStorage, ids, default).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<T>> LoadManyAsync<T>(IEnumerable<Guid> ids) where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T)).ConfigureAwait(false);
        return await ExecuteLoadManyAsync(StorageFor<T, Guid>(), ids.ToArray(), default).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, params Guid[] ids) where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T), token).ConfigureAwait(false);
        return await ExecuteLoadManyAsync(StorageFor<T, Guid>(), ids, token).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, IEnumerable<Guid> ids)
        where T : notnull
    {
        assertNotDisposed();
        await Database.EnsureStorageExistsAsync(typeof(T), token).ConfigureAwait(false);
        return await ExecuteLoadManyAsync(StorageFor<T, Guid>(), ids.ToArray(), token).ConfigureAwait(false);
    }
}
