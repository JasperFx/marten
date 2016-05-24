using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Schema.Hierarchies
{
    public class SubClassDocumentStorage<T, TBase>
        : IDocumentStorage, IResolver<T>
        where T : class, TBase
        where TBase : class
    {
        private readonly IDocumentStorage _parent;

        public SubClassDocumentStorage(IDocumentStorage parent)
        {
            _parent = parent;
        }

        public Type DocumentType => typeof(T);
        public NpgsqlDbType IdType => _parent.IdType;

        public NpgsqlCommand LoaderCommand(object id)
        {
            return _parent.LoaderCommand(id);
        }

        public NpgsqlCommand DeleteCommandForId(object id)
        {
            return _parent.DeleteCommandForId(id);
        }

        public NpgsqlCommand DeleteCommandForEntity(object entity)
        {
            return _parent.DeleteCommandForEntity(entity);
        }

        public NpgsqlCommand LoadByArrayCommand<TKey>(TKey[] ids)
        {
            return _parent.LoadByArrayCommand(ids);
        }

        public object Identity(object document)
        {
            return _parent.Identity(document);
        }

        public void RegisterUpdate(UpdateBatch batch, object entity)
        {
            _parent.RegisterUpdate(batch, entity);
        }

        public void RegisterUpdate(UpdateBatch batch, object entity, string json)
        {
            _parent.RegisterUpdate(batch, entity, json);
        }

        public void Remove(IIdentityMap map, object entity)
        {
            _parent.Remove(map, entity);
        }

        public void Delete(IIdentityMap map, object id)
        {
            _parent.Delete(map, id);
        }

        public void Store(IIdentityMap map, object id, object entity)
        {
            _parent.Store(map, id, entity);
        }

        public T Resolve(int startingIndex, DbDataReader reader, IIdentityMap map)
        {
            var json = reader.GetString(startingIndex);
            var id = reader[startingIndex + 1];

            var version = reader.GetFieldValue<Guid>(3);

            return map.Get<TBase>(id, typeof(T), json, version) as T;
        }

        public async Task<T> ResolveAsync(int startingIndex, DbDataReader reader, IIdentityMap map,
            CancellationToken token)
        {
            var json = await reader.GetFieldValueAsync<string>(startingIndex, token).ConfigureAwait(false);
            var id = await reader.GetFieldValueAsync<object>(startingIndex + 1, token).ConfigureAwait(false);

            var version = await reader.GetFieldValueAsync<Guid>(3, token).ConfigureAwait(false);

            return map.Get<TBase>(id, typeof(T), json, version) as T;
        }


        public FetchResult<T> Fetch(DbDataReader reader, ISerializer serializer)
        {
            if (!reader.Read()) return null;

            var json = reader.GetString(0);
            var doc = serializer.FromJson<T>(json);

            var version = reader.GetFieldValue<Guid>(3);

            return new FetchResult<T>(doc, json, version);
        }

        // TODO -- this is all duplicated from Resolver<T>, fix that.
        public async Task<FetchResult<T>> FetchAsync(DbDataReader reader, ISerializer serializer, CancellationToken token)
        {
            var found = await reader.ReadAsync(token).ConfigureAwait(false);
            if (!found) return null;

            var json = await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false);
            var doc = serializer.FromJson<T>(json);

            var version = await reader.GetFieldValueAsync<Guid>(3, token).ConfigureAwait(false);

            return new FetchResult<T>(doc, json, version);
        }

        public T Resolve(IIdentityMap map, ILoader loader, object id)
        {
            // TODO -- watch it here if it's the wrong type
            return map.Get(id, () => loader.LoadDocument<TBase>(id)) as T;
        }

        public Task<T> ResolveAsync(IIdentityMap map, ILoader loader, CancellationToken token, object id)
        {
            return map.GetAsync(id, tk => loader.LoadDocumentAsync<TBase>(id, tk), token)
                .ContinueWith(x => x.Result as T, token);
        }

        public T Resolve(DbDataReader reader, ISerializer serializer)
        {
            var json = reader.GetString(0);

            // TODO -- what if it's not the right type?
            return serializer.FromJson<T>(json);
        }
    }
}