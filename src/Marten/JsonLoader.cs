#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal.Sessions;
using Marten.Linq.QueryHandlers;

namespace Marten;

internal class JsonLoader: IJsonLoader
{
    private readonly QuerySession _session;

    public JsonLoader(QuerySession session)
    {
        _session = session;
    }

    public Task<string?> FindByIdAsync<T>(string id, CancellationToken token) where T : class
    {
        return findJsonByIdAsync<T, string>(id, token);
    }

    public Task<string?> FindByIdAsync<T>(int id, CancellationToken token = new()) where T : class
    {
        return findJsonByIdAsync<T, int>(id, token);
    }

    public Task<string?> FindByIdAsync<T>(long id, CancellationToken token = new()) where T : class
    {
        return findJsonByIdAsync<T, long>(id, token);
    }

    public Task<string?> FindByIdAsync<T>(Guid id, CancellationToken token = new()) where T : class
    {
        return findJsonByIdAsync<T, Guid>(id, token);
    }

    public Task<bool> StreamById<T>(int id, Stream destination, CancellationToken token = default) where T : class
    {
        return streamJsonById<T, int>(id, destination, token);
    }

    public Task<bool> StreamById<T>(long id, Stream destination, CancellationToken token = default) where T : class
    {
        return streamJsonById<T, long>(id, destination, token);
    }

    public Task<bool> StreamById<T>(string id, Stream destination, CancellationToken token = default) where T : class
    {
        return streamJsonById<T, string>(id, destination, token);
    }

    public Task<bool> StreamById<T>(Guid id, Stream destination, CancellationToken token = default) where T : class
    {
        return streamJsonById<T, Guid>(id, destination, token);
    }

    public Task<string?> FindJsonByIdAsync<T>(int id, CancellationToken token) where T : class
    {
        return findJsonByIdAsync<T, int>(id, token);
    }

    private async Task<string?> findJsonByIdAsync<T, TId>(TId id, CancellationToken token)
        where T : notnull where TId : notnull
    {
        await _session.Database.EnsureStorageExistsAsync(typeof(T), token).ConfigureAwait(false);

        var storage = _session.QueryStorageFor<T, TId>();
        var command = storage.BuildLoadCommand(id, _session.TenantId);

        return await _session.LoadOneAsync(command, LinqConstants.StringValueSelector, token).ConfigureAwait(false);
    }

    private async Task<bool> streamJsonById<T, TId>(TId id, Stream destination, CancellationToken token)
        where T : class where TId : notnull
    {
        await _session.Database.EnsureStorageExistsAsync(typeof(T), token).ConfigureAwait(false);
        var storage = _session.QueryStorageFor<T, TId>();
        var command = storage.BuildLoadCommand(id, _session.TenantId);

        return await _session.StreamOne(command, destination, token).ConfigureAwait(false);
    }
}
