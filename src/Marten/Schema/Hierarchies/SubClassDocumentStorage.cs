using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Services;
using Marten.Storage;
using Marten.Util;
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
        private readonly MetadataProjector<T> _metadataProjector;

        public SubClassDocumentStorage(IDocumentStorage<TBase> parent, SubClassMapping mapping)
        {
            _parent = parent;
            _mapping = mapping;
            _metadataProjector = new MetadataProjector<T>(mapping.Parent);
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
            _parent.RegisterUpdate(tenantIdOverride, updateStyle, batch, entity, typeof(T));
        }

        public void RegisterUpdate(string tenantIdOverride, UpdateStyle updateStyle, UpdateBatch batch, object entity, string json)
        {
            _parent.RegisterUpdate(tenantIdOverride, updateStyle, batch, entity, typeof(T));
        }

        public void RegisterUpdate(string tenantIdOverride, UpdateStyle updateStyle, UpdateBatch batch, object entity, Type entityType)
        {
            _parent.RegisterUpdate(tenantIdOverride, updateStyle, batch, entity, entityType);
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
            var offset = 0;
            var id = reader[startingIndex + ++offset];
            var typeAlias = reader.GetFieldValue<string>(startingIndex + ++offset);

            var version = reader.GetFieldValue<Guid>(startingIndex + ++offset);
            var lastMod = reader.GetValue(startingIndex + ++offset).MapToDateTime();
            var dotNetType = reader.GetFieldValue<string>(startingIndex + ++offset);

            var deleted = false;
            DateTime? deletedAt = null;
            if (_mapping.DeleteStyle == DeleteStyle.SoftDelete)
            {
                deleted = reader.GetFieldValue<bool>(startingIndex + ++offset);
                if (!reader.IsDBNull(startingIndex + ++offset))
                {
                    deletedAt = reader.GetValue(startingIndex + offset).MapToDateTime();
                }
            }

            string tenantId = null;
            if (_mapping.TenancyStyle == TenancyStyle.Conjoined)
            {
                tenantId = reader.GetFieldValue<string>(startingIndex + ++offset);
            }

            var metadata = new DocumentMetadata(lastMod, version, dotNetType, typeAlias, deleted, deletedAt)
            {
                TenantId = tenantId
            };

            var actualType = _mapping.TypeFor(typeAlias);
            var json = reader.GetTextReader(startingIndex);
            return map.Get<TBase>(id, actualType, json, version, t => _metadataProjector.ProjectTo(t as T, metadata)) as T;

        }

        public async Task<T> ResolveAsync(int startingIndex, DbDataReader reader, IIdentityMap map,
            CancellationToken token)
        {
            var offset = 0;
            var id = await reader.GetFieldValueAsync<object>(startingIndex + ++offset, token).ConfigureAwait(false);

            var typeAlias = await reader.GetFieldValueAsync<string>(startingIndex + ++offset, token).ConfigureAwait(false);

            var version = await reader.GetFieldValueAsync<Guid>(startingIndex + ++offset, token).ConfigureAwait(false);
            var lastMod = (await reader.GetFieldValueAsync<object>(startingIndex + ++offset, token).ConfigureAwait(false)).MapToDateTime();
            var dotNetType = await reader.GetFieldValueAsync<string>(startingIndex + ++offset, token).ConfigureAwait(false);

            var deleted = false;
            DateTime? deletedAt = null;
            if (_mapping.DeleteStyle == DeleteStyle.SoftDelete)
            {
                deleted = await reader.GetFieldValueAsync<bool>(startingIndex + ++offset, token).ConfigureAwait(false);
                if (!await reader.IsDBNullAsync(startingIndex + ++offset, token).ConfigureAwait(false))
                {
                    deletedAt = (await reader.GetFieldValueAsync<object>(startingIndex + offset, token).ConfigureAwait(false)).MapToDateTime();
                }
            }

            string tenantId = null;
            if (_mapping.TenancyStyle == TenancyStyle.Conjoined)
            {
                tenantId = await reader.GetFieldValueAsync<string>(startingIndex + ++offset, token).ConfigureAwait(false);
            }

            var metadata = new DocumentMetadata(lastMod, version, dotNetType, typeAlias, deleted, deletedAt)
            {
                TenantId = tenantId
            };

            var json = await reader.As<NpgsqlDataReader>().GetTextReaderAsync(startingIndex).ConfigureAwait(false);

            return map.Get<TBase>(id, _mapping.TypeFor(typeAlias), json, version, t => _metadataProjector.ProjectTo(t as T, metadata)) as T;
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
