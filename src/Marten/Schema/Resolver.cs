using System;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using FastExpressionCompiler;
using Marten.Linq;
using Marten.Schema.Arguments;
using Marten.Schema.Identity;
using Marten.Services;
using Marten.Services.Deletes;
using Marten.Storage;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Schema
{
    public class DocumentStorage<T> : IDocumentStorage<T> where T : class
    {
        private readonly string _deleteSql;
        private readonly Func<T, object> _identity;
        private readonly string _loadArraySql;
        private readonly string _loaderSql;
        private readonly ISerializer _serializer;
        private readonly DocumentMapping _mapping;
        private readonly bool _useCharBufferPooling;
        private readonly DbObjectName _upsertName;
        private readonly Action<SprocCall, T, UpdateBatch, DocumentMapping, Guid?, Guid, string> _sprocWriter;

        public DocumentStorage(ISerializer serializer, DocumentMapping mapping, bool useCharBufferPooling)
        {
            _serializer = serializer;
            _mapping = mapping;
            _useCharBufferPooling = useCharBufferPooling;
            IdType = TypeMappings.ToDbType(mapping.IdMember.GetMemberType());


            _loaderSql =
                $"select {_mapping.SelectFields().Join(", ")} from {_mapping.Table.QualifiedName} as d where id = :id";
            _deleteSql = $"delete from {_mapping.Table.QualifiedName} where id = :id";
            _loadArraySql =
                $"select {_mapping.SelectFields().Join(", ")} from {_mapping.Table.QualifiedName} as d where id = ANY(:ids)";


            _identity = LambdaBuilder.Getter<T, object>(mapping.IdMember);

            _sprocWriter = buildSprocWriter(mapping);
            

            _upsertName = mapping.UpsertFunction;

            if (mapping.DeleteStyle == DeleteStyle.Remove)
            {
                DeleteByIdSql = $"delete from {_mapping.Table.QualifiedName} where id = ?";
                DeleteByWhereSql = $"delete from {_mapping.Table.QualifiedName} as d where ?";
            }
            else
            {
                DeleteByIdSql = $"update {_mapping.Table.QualifiedName} set {DocumentMapping.DeletedColumn} = True, {DocumentMapping.DeletedAtColumn} = now() where id = ?";
                DeleteByWhereSql = $"update {_mapping.Table.QualifiedName} as d set {DocumentMapping.DeletedColumn} = True, {DocumentMapping.DeletedAtColumn} = now() where ?";
            }
        }

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
                return x.CompileUpdateExpression(_serializer.EnumStorage, call, doc, batch, mappingParam, currentVersion, newVersion, tenantId, _useCharBufferPooling);
            });

            var block = Expression.Block(arguments);

            var lambda = Expression.Lambda<Action<SprocCall, T, UpdateBatch, DocumentMapping, Guid?, Guid, string>>(block,
                new ParameterExpression[]
                {
                    call, doc, batch, mappingParam, currentVersion, newVersion, tenantId
                });

            return ExpressionCompiler.Compile<Action<SprocCall, T, UpdateBatch, DocumentMapping, Guid?, Guid, string>>(lambda);
        }

        public Type DocumentType => _mapping.DocumentType;
        public NpgsqlDbType IdType { get; }


        public virtual T Resolve(int startingIndex, DbDataReader reader, IIdentityMap map)
        {
            if (reader.IsDBNull(startingIndex)) return null;

            var json = reader.GetTextReader(startingIndex);
            var id = reader[startingIndex + 1];

            var version = reader.GetFieldValue<Guid>(startingIndex + 2);

            return map.Get<T>(id, json, version);
        }

        public virtual async Task<T> ResolveAsync(int startingIndex, DbDataReader reader, IIdentityMap map,
            CancellationToken token)
        {
            if (await reader.IsDBNullAsync(startingIndex, token).ConfigureAwait(false)) return null;


            var json = reader.GetTextReader(startingIndex);
            //var json = await reader.GetFieldValueAsync<string>(startingIndex, token).ConfigureAwait(false);

            var id = await reader.GetFieldValueAsync<object>(startingIndex + 1, token).ConfigureAwait(false);

            var version = await reader.GetFieldValueAsync<Guid>(startingIndex + 2, token).ConfigureAwait(false);

            return map.Get<T>(id, json, version);
        }

        public T Resolve(IIdentityMap map, ILoader loader, object id)
        {
            return map.Get(id, () => loader.LoadDocument<T>(id));
        }

        public Task<T> ResolveAsync(IIdentityMap map, ILoader loader, CancellationToken token, object id)
        {
            return map.GetAsync(id, async tk => await loader.LoadDocumentAsync<T>(id, tk).ConfigureAwait(false), token);
        }

        public virtual FetchResult<T> Fetch(DbDataReader reader, ISerializer serializer)
        {
            var found = reader.Read();
            if (!found) return null;

            var json = reader.GetTextReader(0);
            var doc = serializer.FromJson<T>(json);

            var version = reader.GetFieldValue<Guid>(2);

            return new FetchResult<T>(doc, json, version);
        }

        public virtual async Task<FetchResult<T>> FetchAsync(DbDataReader reader, ISerializer serializer, CancellationToken token)
        {
            var found = await reader.ReadAsync(token).ConfigureAwait(false);
            if (!found) return null;

            var json = reader.GetTextReader(0);
            //var json = await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false);
            var doc = serializer.FromJson<T>(json);

            var version = await reader.GetFieldValueAsync<Guid>(2, token).ConfigureAwait(false);

            return new FetchResult<T>(doc, json, version);
        }


        public NpgsqlCommand LoaderCommand(object id)
        {
            return new NpgsqlCommand(_loaderSql).With("id", id);
        }

        public NpgsqlCommand LoadByArrayCommand<TKey>(TenancyStyle tenancyStyle, TKey[] ids)
        {
            var sql = _loadArraySql;
            if (tenancyStyle == TenancyStyle.Conjoined)
            {
                sql += $" and {TenantIdColumn.Name} = :{TenantIdArgument.ArgName}";
            }

            return new NpgsqlCommand(sql).With("ids", ids);
        }

        public object Identity(object document)
        {
            return _identity((T) document);
        }

        public void RegisterUpdate(UpdateBatch batch, object entity)
        {
            var json = batch.Serializer.ToJson(entity);
            RegisterUpdate(batch, entity, json);
        }

        public void RegisterUpdate(UpdateBatch batch, object entity, string json)
        {
            var newVersion = CombGuidIdGeneration.NewGuid();
            var currentVersion = batch.Versions.Version<T>(Identity(entity));

            ICallback callback = null;
            if (_mapping.UseOptimisticConcurrency)
            {
                callback = new OptimisticConcurrencyCallback<T>(Identity(entity), batch.Versions, newVersion, currentVersion);
            }


            var call = batch.Sproc(_upsertName, callback);

            

            _sprocWriter(call, (T) entity, batch, _mapping, currentVersion, newVersion, batch.TenantId);
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
            map.Store<T>(id, (T) entity);
        }

        public IStorageOperation DeletionForId(TenancyStyle tenancyStyle, object id)
        {
            return new DeleteById(tenancyStyle, DeleteByIdSql, this, id);
        }

        public IStorageOperation DeletionForEntity(TenancyStyle tenancyStyle, object entity)
        {
            return new DeleteById(tenancyStyle, DeleteByIdSql, this, Identity(entity), entity);
        }

        public IStorageOperation DeletionForWhere(IWhereFragment @where, TenancyStyle tenancyStyle)
        {
            return new DeleteWhere(typeof(T), DeleteByWhereSql, @where, tenancyStyle);
        }
    }
}