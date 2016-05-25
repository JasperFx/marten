using System;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Schema.Identity;
using Marten.Services;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Schema
{
    public class Resolver<T> : IResolver<T>, IDocumentStorage, IDocumentUpsert where T : class
    {
        private readonly string _deleteSql;
        private readonly Func<T, object> _identity;
        private readonly string _loadArraySql;
        private readonly string _loaderSql;
        private readonly ISerializer _serializer;
        private readonly DocumentMapping _mapping;
        private readonly FunctionName _upsertName;
        private readonly Action<SprocCall, T, UpdateBatch, DocumentMapping, Guid?, Guid> _sprocWriter;

        public Resolver(ISerializer serializer, DocumentMapping mapping)
        {
            _serializer = serializer;
            _mapping = mapping;
            IdType = TypeMappings.ToDbType(mapping.IdMember.GetMemberType());


            _loaderSql =
                $"select {_mapping.SelectFields().Join(", ")} from {_mapping.Table.QualifiedName} as d where id = :id";
            _deleteSql = $"delete from {_mapping.Table.QualifiedName} where id = :id";
            _loadArraySql =
                $"select {_mapping.SelectFields().Join(", ")} from {_mapping.Table.QualifiedName} as d where id = ANY(:ids)";


            _identity = LambdaBuilder.Getter<T, object>(mapping.IdMember);

            _sprocWriter = buildSprocWriter(mapping);
            

            _upsertName = mapping.UpsertFunction;
        }

        private Action<SprocCall, T, UpdateBatch, DocumentMapping, Guid?, Guid> buildSprocWriter(DocumentMapping mapping)
        {
            var call = Expression.Parameter(typeof(SprocCall), "call");
            var doc = Expression.Parameter(typeof(T), "doc");
            var batch = Expression.Parameter(typeof(UpdateBatch), "batch");
            var mappingParam = Expression.Parameter(typeof(DocumentMapping), "mapping");

            var currentVersion = Expression.Parameter(typeof(Guid?), "currentVersion");
            var newVersion = Expression.Parameter(typeof(Guid), "newVersion");

            var arguments = new UpsertFunction(mapping).OrderedArguments().Select(x =>
            {
                return x.CompileUpdateExpression(_serializer.EnumStorage, call, doc, batch, mappingParam, currentVersion, newVersion);
            });

            var block = Expression.Block(arguments);

            var lambda = Expression.Lambda<Action<SprocCall, T, UpdateBatch, DocumentMapping, Guid?, Guid>>(block,
                new ParameterExpression[]
                {
                    call, doc, batch, mappingParam, currentVersion, newVersion
                });


            return lambda.Compile();
        }

        public Type DocumentType => _mapping.DocumentType;
        public NpgsqlDbType IdType { get; }


        public virtual T Resolve(int startingIndex, DbDataReader reader, IIdentityMap map)
        {
            if (reader.IsDBNull(startingIndex)) return null;

            var json = reader.GetString(startingIndex);
            var id = reader[startingIndex + 1];

            var version = reader.GetFieldValue<Guid>(2);

            return map.Get<T>(id, json, version);
        }

        public virtual async Task<T> ResolveAsync(int startingIndex, DbDataReader reader, IIdentityMap map,
            CancellationToken token)
        {
            if (await reader.IsDBNullAsync(startingIndex, token).ConfigureAwait(false)) return null;

            var json = await reader.GetFieldValueAsync<string>(startingIndex, token).ConfigureAwait(false);

            var id = await reader.GetFieldValueAsync<object>(startingIndex + 1, token).ConfigureAwait(false);

            var version = await reader.GetFieldValueAsync<Guid>(2, token).ConfigureAwait(false);

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

            var json = reader.GetString(0);
            var doc = serializer.FromJson<T>(json);

            var version = reader.GetFieldValue<Guid>(2);

            return new FetchResult<T>(doc, json, version);
        }

        public virtual async Task<FetchResult<T>> FetchAsync(DbDataReader reader, ISerializer serializer, CancellationToken token)
        {
            var found = await reader.ReadAsync(token).ConfigureAwait(false);
            if (!found) return null;

            var json = await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false);
            var doc = serializer.FromJson<T>(json);

            var version = await reader.GetFieldValueAsync<Guid>(2, token).ConfigureAwait(false);

            return new FetchResult<T>(doc, json, version);
        }


        public NpgsqlCommand LoaderCommand(object id)
        {
            return new NpgsqlCommand(_loaderSql).With("id", id);
        }

        public NpgsqlCommand DeleteCommandForId(object id)
        {
            return new NpgsqlCommand(_deleteSql).With("id", id);
        }

        public NpgsqlCommand DeleteCommandForEntity(object entity)
        {
            return DeleteCommandForId(_identity((T) entity));
        }

        public NpgsqlCommand LoadByArrayCommand<TKey>(TKey[] ids)
        {
            return new NpgsqlCommand(_loadArraySql).With("ids", ids);
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
            var newVersion = CombGuidIdGeneration.New();
            var currentVersion = batch.Versions.Version<T>(Identity(entity));

            ICallback callback = null;
            if (_mapping.UseOptimisticConcurrency && currentVersion.HasValue)
            {
                callback = new OptimisticConcurrencyCallback<T>(Identity(entity), batch.Versions, newVersion, currentVersion.Value);
            }


            var call = batch.Sproc(_upsertName, callback);

            

            _sprocWriter(call, (T) entity, batch, _mapping, currentVersion, newVersion);
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
    }
}