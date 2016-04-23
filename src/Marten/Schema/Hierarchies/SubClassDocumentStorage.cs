using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Services;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Schema.Hierarchies
{
    public class SubClassDocumentStorage<T, TBase> 
        : IDocumentStorage, IResolver<T>, IBulkLoader<T>, IdAssignment<T>
        where T : class, TBase
        where TBase : class 
    {
        private readonly IDocumentStorage _parent;
        private readonly IdAssignment<TBase> _parentIdAssignment;
        private readonly IBulkLoader<TBase> _parentBulkLoader;

        public SubClassDocumentStorage(IDocumentStorage parent)
        {
            _parent = parent;
            _parentIdAssignment = _parent.As<IdAssignment<TBase>>();
            _parentBulkLoader = _parent.As<IBulkLoader<TBase>>();
        }

        public Type DocumentType => typeof (T);
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

        public T Resolve(int startingIndex, DbDataReader reader, IIdentityMap map)
        {
            var json = reader.GetString(startingIndex);
            var id = reader[startingIndex + 1];
            
            return map.Get<TBase>(id, typeof(T), json) as T;
        }

        public async Task<T> ResolveAsync(int startingIndex, DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            var json = await reader.GetFieldValueAsync<string>(startingIndex, token).ConfigureAwait(false);
            var id = await reader.GetFieldValueAsync<object>(startingIndex + 1, token).ConfigureAwait(false);

            return map.Get<TBase>(id, typeof(T), json) as T;
        }

        public T Build(DbDataReader reader, ISerializer serializer)
        {
            // TODO -- watch this, will need to validate that it's the right type first
            return serializer.FromJson<T>(reader.GetString(0));
        }

        public T Resolve(DbDataReader reader, ISerializer serializer)
        {
            var json = reader.GetString(0);

            // TODO -- what if it's not the right type?
            return serializer.FromJson<T>(json);
        }



        public void Load(ISerializer serializer, NpgsqlConnection conn, IEnumerable<T> documents)
        {
            _parentBulkLoader.Load(serializer, conn, documents);
        }

        public object Assign(T document, out bool assigned)
        {
            return _parentIdAssignment.Assign(document, out assigned);
        }

        public void Assign(T document, object id)
        {
            throw new NotSupportedException();
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

        public T Resolve(IIdentityMap map, ILoader loader, object id)
        {
            // TODO -- watch it here if it's the wrong type
            return map.Get(id, () => loader.LoadDocument<TBase>(id)) as T;
        }

        public Task<T> ResolveAsync(IIdentityMap map, ILoader loader, CancellationToken token, object id)
        {
            return map.GetAsync(id, (tk => loader.LoadDocumentAsync<TBase>(id, tk)), token)
                .ContinueWith(x => x.Result as T, token);
        }


    }
}