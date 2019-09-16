using System;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Schema.Identity;
using Marten.Services;
using Marten.Services.Deletes;
using Marten.Storage;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Schema
{
    public class DocumentStorage<T>: IDocumentStorage<T> where T : class
    {
        private readonly Func<T, object> _identity;
        private readonly string _loadArraySql;
        private readonly string _loaderSql;
        private readonly DocumentMapping _mapping;
        private readonly DbObjectName _upsertName;
        private readonly Action<SprocCall, T, UpdateBatch, DocumentMapping, Guid?, Guid, string> _sprocWriter;

        protected readonly MetadataProjector<T> MetadataProjector;

        public DocumentStorage(DocumentMapping mapping)
        {
            _mapping = mapping;
            IdType = TypeMappings.ToDbType(mapping.IdMember.GetMemberType());

            _loaderSql =
                $"select {_mapping.SelectFields().Join(", ")} from {_mapping.Table.QualifiedName} as d where id = :id";

            _loadArraySql =
                $"select {_mapping.SelectFields().Join(", ")} from {_mapping.Table.QualifiedName} as d where id = ANY(:ids)";

            if (mapping.TenancyStyle == TenancyStyle.Conjoined)
            {
                _loaderSql += $" and {TenantWhereFragment.Filter}";
                _loadArraySql += $" and {TenantWhereFragment.Filter}";
            }

            _identity = LambdaBuilder.Getter<T, object>(mapping.IdMember);

            _sprocWriter = buildSprocWriter(mapping);

            _upsertName = mapping.UpsertFunction;

            if (mapping.DeleteStyle == DeleteStyle.Remove)
            {
                DeleteByIdSql = $"delete from {_mapping.Table.QualifiedName} as d where id = ?";
                DeleteByWhereSql = $"delete from {_mapping.Table.QualifiedName} as d where ?";
            }
            else
            {
                string softDeleteJsonb = null;
                if (mapping.IsSoftDeletedMember != null)
                {
                    softDeleteJsonb = $"jsonb_set(data,'{{{mapping.IsSoftDeletedMember.Name.FormatCase(mapping.Casing)}}}','\"true\"'::jsonb)::jsonb";
                }
                if (mapping.SoftDeletedAtMember != null)
                {
                    softDeleteJsonb = $"jsonb_set({(softDeleteJsonb ?? "data")},'{{{mapping.SoftDeletedAtMember.Name.FormatCase(mapping.Casing)}}}', to_jsonb(now()))::jsonb";
                }
                var setJsonb = softDeleteJsonb == null ? "" : $", data = {softDeleteJsonb}";

                DeleteByIdSql = $"update {_mapping.Table.QualifiedName} as d set {DocumentMapping.DeletedColumn} = true, {DocumentMapping.DeletedAtColumn} = now(){setJsonb} where id = ?";
                DeleteByWhereSql = $"update {_mapping.Table.QualifiedName} as d set {DocumentMapping.DeletedColumn} = true, {DocumentMapping.DeletedAtColumn} = now(){setJsonb} where ?";
            }

            MetadataProjector = new MetadataProjector<T>(mapping);
        }

        public Type TopLevelBaseType => DocumentType;

        public string DeleteByWhereSql { get; }

        public string DeleteByIdSql { get; }

        private Action<SprocCall, T, UpdateBatch, DocumentMapping, Guid?, Guid, string> buildSprocWriter(DocumentMapping mapping)
        {
            var call = Expression.Parameter(typeof(SprocCall), "call");
            var doc = Expression.Parameter(typeof(T), "doc");
            var batch = Expression.Parameter(typeof(UpdateBatch), "batch");
            var mappingParam = Expression.Parameter(typeof(DocumentMapping), "mapping");

            var currentVersion = Expression.Parameter(typeof(Guid?), "currentVersion");
            var newVersion = Expression.Parameter(typeof(Guid), "newVersion");

            var tenantId = Expression.Parameter(typeof(string), "tenantId");

            var arguments = new UpsertFunction(mapping).OrderedArguments().Select(x =>
            {
                return x.CompileUpdateExpression(_mapping.DuplicatedFieldEnumStorage, call, doc, batch, mappingParam, currentVersion, newVersion, tenantId, true);
            });

            var block = Expression.Block(arguments);

            var lambda = Expression.Lambda<Action<SprocCall, T, UpdateBatch, DocumentMapping, Guid?, Guid, string>>(block,
                new ParameterExpression[]
                {
                    call, doc, batch, mappingParam, currentVersion, newVersion, tenantId
                });

            return ExpressionCompiler.Compile<Action<SprocCall, T, UpdateBatch, DocumentMapping, Guid?, Guid, string>>(lambda);
        }

        public TenancyStyle TenancyStyle => _mapping.TenancyStyle;
        public Type DocumentType => _mapping.DocumentType;
        public NpgsqlDbType IdType { get; }

        public virtual T Resolve(int startingIndex, DbDataReader reader, IIdentityMap map)
        {
            if (reader.IsDBNull(startingIndex))
                return null;

            var offset = 0;
            var id = reader[startingIndex + ++offset];

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

            var metadata = new DocumentMetadata(lastMod, version, dotNetType, null, deleted, deletedAt)
            {
                TenantId = tenantId
            };

            using (var json = reader.GetTextReader(startingIndex))
            {
                return map.Get<T>(id, typeof(T), json, version, t => MetadataProjector.ProjectTo(t, metadata));
            }
        }

        public virtual async Task<T> ResolveAsync(int startingIndex, DbDataReader reader, IIdentityMap map,
            CancellationToken token)
        {
            if (await reader.IsDBNullAsync(startingIndex, token).ConfigureAwait(false))
                return null;

            var offset = 0;
            var id = await reader.GetFieldValueAsync<object>(startingIndex + ++offset, token).ConfigureAwait(false);

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

            var metadata = new DocumentMetadata(lastMod, version, dotNetType, null, deleted, deletedAt)
            {
                TenantId = tenantId
            };

            using (var json = await reader.As<NpgsqlDataReader>().GetTextReaderAsync(startingIndex).ConfigureAwait(false))
            {
                return map.Get<T>(id, typeof(T), json, version, t => MetadataProjector.ProjectTo(t, metadata));
            }
        }

        public T Resolve(IIdentityMap map, IQuerySession session, object id)
        {
            if (map.Has<T>(id))
                return map.Retrieve<T>(id);

            var cmd = LoaderCommand(id);
            cmd.AddTenancy(session.Tenant);
            cmd.Connection = session.Connection;
            using (var reader = cmd.ExecuteReader())
            {
                return Fetch(id, reader, map);
            }
        }

        public async Task<T> ResolveAsync(IIdentityMap map, IQuerySession session, CancellationToken token, object id)
        {
            if (map.Has<T>(id))
                return map.Retrieve<T>(id);

            var cmd = LoaderCommand(id);
            cmd.AddTenancy(session.Tenant);
            cmd.Connection = session.Connection;

            using (var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                return await FetchAsync(id, reader, map, token).ConfigureAwait(false);
            }
        }

        public virtual T Fetch(object id, DbDataReader reader, IIdentityMap map)
        {
            var found = reader.Read();
            if (!found)
                return null;

            return Resolve(0, reader, map);
        }

        public virtual async Task<T> FetchAsync(object id, DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            var found = await reader.ReadAsync(token).ConfigureAwait(false);
            if (!found)
                return null;

            return await ResolveAsync(0, reader, map, token).ConfigureAwait(false);
        }

        public NpgsqlCommand LoaderCommand(object id)
        {
            return new NpgsqlCommand(_loaderSql).With("id", id);
        }

        public NpgsqlCommand LoadByArrayCommand<TKey>(TKey[] ids)
        {
            var sql = _loadArraySql;

            return new NpgsqlCommand(sql).With("ids", ids);
        }

        public object Identity(object document)
        {
            return _identity((T)document);
        }

        public void RegisterUpdate(string tenantIdOverride, UpdateStyle updateStyle, UpdateBatch batch, object entity)
        {
            RegisterUpdate(tenantIdOverride, updateStyle, batch, entity, typeof(T));
        }

        private DbObjectName determineDbObjectName(UpdateStyle updateStyle, UpdateBatch batch)
        {
            switch (updateStyle)
            {
                case UpdateStyle.Upsert:
                    if (_mapping.UseOptimisticConcurrency && batch.Concurrency == ConcurrencyChecks.Disabled)
                    {
                        return _mapping.OverwriteFunction;
                    }

                    return _mapping.UpsertFunction;

                case UpdateStyle.Insert:
                    return _mapping.InsertFunction;

                case UpdateStyle.Update:
                    return _mapping.UpdateFunction;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public void RegisterUpdate(string tenantIdOverride, UpdateStyle updateStyle, UpdateBatch batch, object entity, string json)
        {
            RegisterUpdate(tenantIdOverride, updateStyle, batch, entity, typeof(T));
        }

        public void RegisterUpdate(string tenantIdOverride, UpdateStyle updateStyle, UpdateBatch batch, object entity, Type entityType)
        {
            var newVersion = CombGuidIdGeneration.NewGuid();
            var currentVersion = batch.Versions.Version<T>(Identity(entity));
            var tenantId = tenantIdOverride ?? batch.TenantId;

            var cachedMetadata = batch.MetadataCache?.MetadataFor(typeof(T), Identity(entity));
            if (cachedMetadata != null)
            {
                MetadataProjector.Update(entity.As<T>(),
                    new DocumentMetadata(DateTime.UtcNow, newVersion, cachedMetadata.DotNetType, cachedMetadata.DocumentType, cachedMetadata.Deleted, cachedMetadata.DeletedAt)
                    {
                        TenantId = cachedMetadata.TenantId
                    });
            }
            else
            {
                MetadataProjector.ProjectTo(entity.As<T>(),
                    new DocumentMetadata(DateTime.UtcNow, newVersion, entityType.FullName, _mapping.AliasFor(entityType), false, null)
                    {
                        TenantId = tenantId
                    });
            }

            ICallback callback = null;
            IExceptionTransform exceptionTransform = null;
            var sprocName = determineDbObjectName(updateStyle, batch);


            if (_mapping.UseOptimisticConcurrency)
            {
                void setVersion(Guid version) => MetadataProjector.UpdateVersion(entity.As<T>(), version);

                callback = new OptimisticConcurrencyCallback<T>(batch.Concurrency, Identity(entity), batch.Versions, newVersion,
                    currentVersion, setVersion);
            }

            if (!_mapping.UseOptimisticConcurrency && updateStyle == UpdateStyle.Update)
            {
                callback = new UpdateDocumentCallback<T>(Identity(entity));
            }

            if (updateStyle == UpdateStyle.Insert)
            {
                exceptionTransform = new InsertExceptionTransform<T>(Identity(entity), _mapping.Table.Name);
            }

            var call = batch.Sproc(sprocName, callback, exceptionTransform);

            _sprocWriter(call, (T)entity, batch, _mapping, currentVersion, newVersion, tenantId);
        }

        public void Remove(IIdentityMap map, object entity)
        {
            var id = Identity(entity);
            map.Remove<T>(id);
        }

        public void Delete(IIdentityMap map, object id)
        {
            map.Remove<T>(id);
        }

        public void Store(IIdentityMap map, object id, object entity)
        {
            map.Store<T>(id, (T)entity);
        }

        public IStorageOperation DeletionForId(object id)
        {
            return new DeleteById(_mapping.TenancyStyle, DeleteByIdSql, this, id);
        }

        public IStorageOperation DeletionForEntity(object entity)
        {
            return new DeleteById(_mapping.TenancyStyle, DeleteByIdSql, this, Identity(entity), entity);
        }

        public IStorageOperation DeletionForWhere(IWhereFragment @where)
        {
            return new DeleteWhere(typeof(T), DeleteByWhereSql, @where, _mapping.TenancyStyle);
        }
    }
}
