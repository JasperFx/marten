using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Services;
using Marten.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Schema.Hierarchies
{
    public class SubClassDocumentStorage<T, TBase>
        : IDocumentStorage<T>
        where T : class, TBase
        where TBase : class
    {
        private readonly IDocumentStorage<TBase> _parent;
        private readonly SubClassMapping _mapping;

        public SubClassDocumentStorage(IDocumentStorage<TBase> parent, SubClassMapping mapping)
        {
            _parent = parent;
            _mapping = mapping;
        }

        public TenancyStyle TenancyStyle => _parent.TenancyStyle;
        public Type DocumentType => typeof(T);
        public Type TopLevelBaseType { get; } = typeof(TBase);
        public NpgsqlDbType IdType => _parent.IdType;

        public NpgsqlCommand LoaderCommand(object id)
        {
            return _parent.LoaderCommand(id);
        }

        public NpgsqlCommand LoadByArrayCommand<TKey>(TKey[] ids)
        {
            return _parent.LoadByArrayCommand(ids);
        }

        public object Identity(object document)
        {
            return _parent.Identity(document);
        }

        public void RegisterUpdate(string tenantIdOverride, UpdateStyle updateStyle, UpdateBatch batch, object entity)
        {
            _parent.RegisterUpdate(tenantIdOverride, updateStyle, batch, entity);
        }

        public void RegisterUpdate(string tenantIdOverride, UpdateStyle updateStyle, UpdateBatch batch, object entity, string json)
        {
            _parent.RegisterUpdate(tenantIdOverride, updateStyle, batch, entity, json);
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

        public IStorageOperation DeletionForId(object id)
        {
            return _parent.DeletionForId(id);
        }

        public IStorageOperation DeletionForEntity(object entity)
        {
            return _parent.DeletionForEntity(entity);
        }

        public IStorageOperation DeletionForWhere(IWhereFragment @where)
        {
            return _parent.DeletionForWhere(@where);
        }

        public T Resolve(int startingIndex, DbDataReader reader, IIdentityMap map)
        {
            var json = reader.GetTextReader(startingIndex);
            var id = reader[startingIndex + 1];

            var version = reader.GetFieldValue<Guid>(3);
            var typeAlias = reader.GetString(startingIndex + 2);

            var actualType = _mapping.TypeFor(typeAlias);

            return map.Get<TBase>(id, actualType, json, version) as T;
        }

        public async Task<T> ResolveAsync(int startingIndex, DbDataReader reader, IIdentityMap map,
            CancellationToken token)
        {
            var json = await reader.As<NpgsqlDataReader>().GetTextReaderAsync(startingIndex).ConfigureAwait(false);
            var id = await reader.GetFieldValueAsync<object>(startingIndex + 1, token).ConfigureAwait(false);

            var version = await reader.GetFieldValueAsync<Guid>(3, token).ConfigureAwait(false);
            var typeAlias = await reader.GetFieldValueAsync<string>(startingIndex + 2, token).ConfigureAwait(false);

            return map.Get<TBase>(id, _mapping.TypeFor(typeAlias), json, version) as T;
        }


        public T Resolve(IIdentityMap map, IQuerySession session, object id)
        {
            return _parent.Resolve(map, session, id) as T;
        }

        public async Task<T> ResolveAsync(IIdentityMap map, IQuerySession session, CancellationToken token, object id)
        {
            var doc = await _parent.ResolveAsync(map, session, token, id).ConfigureAwait(false);
            return doc as T;
        }

        public T Resolve(DbDataReader reader, ISerializer serializer)
        {
            var json = reader.GetTextReader(0);

            // TODO -- what if it's not the right type?
            return serializer.FromJson<T>(json);
        }
    }
}