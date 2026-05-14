#nullable enable
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Internal.Sessions;
using Marten.Linq.QueryHandlers;
using System.Diagnostics.CodeAnalysis;

namespace Marten;

[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
internal class JsonLoader: IJsonLoader
{
    private readonly QuerySession _session;

    public JsonLoader(QuerySession session)
    {
        _session = session;
    }

    public Task<string?> FindByIdAsync<T>(object id, CancellationToken token = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(id);

        var streamer = BuildStreamer<T>(id.GetType());
        return streamer.FindByIdAsync(id, token);
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

    public Task<bool> StreamById<T>(object id, Stream destination, CancellationToken token = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(id);

        var streamer = BuildStreamer<T>(id.GetType());
        return streamer.StreamJsonById(id, destination, token);
    }

    // 9.0 (#4373): delegate-cached IStreamer<T> factory keyed on (T, idType). Both
    // endpoints (FindByIdAsync, StreamById) hit the same cache, so a high-RPS
    // AspNetCore deployment that polls a stable (doc, idType) pair pays one
    // reflection cost at warm-up and zero per request thereafter.
    //
    // GenericFactoryCache doesn't ship a (2 type args, 1 ctor arg) overload yet, so
    // we hold the small per-(T, idType) cache locally — the cache key is sized to the
    // small set of (doc type, identity type) pairs an app actually queries by.
    private static readonly ConcurrentDictionary<(Type Doc, Type Id), Func<JsonLoader, object>> _streamerFactories = new();

    private IStreamer<T> BuildStreamer<T>(Type idType) where T : class
    {
        var factory = _streamerFactories.GetOrAdd(
            (typeof(T), idType),
            static key =>
            {
                var closed = typeof(Streamer<,>).MakeGenericType(key.Doc, key.Id);
                return loader => Activator.CreateInstance(closed, loader)!;
            });
        return (IStreamer<T>)factory(this);
    }

    private interface IStreamer<T>
    {
        Task<bool> StreamJsonById(object id, Stream destination, CancellationToken token);
        Task<string?> FindByIdAsync(object id, CancellationToken token);
    }

    private class Streamer<T, TId>: IStreamer<T> where T : class where TId : notnull
    {
        private readonly JsonLoader _parent;

        public Streamer(JsonLoader parent)
        {
            _parent = parent;
        }

        public Task<string?> FindByIdAsync(object id, CancellationToken token)
        {
            return _parent.findJsonByIdAsync<T, TId>((TId)id, token);
        }

        public Task<bool> StreamJsonById(object id, Stream destination, CancellationToken token)
        {
            return _parent.streamJsonById<T, TId>((TId)id, destination, token);
        }
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
