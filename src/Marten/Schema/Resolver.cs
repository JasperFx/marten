using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Services;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Schema
{
    public class Resolver<T> : IResolver<T> where T : class
    {
        private readonly string _deleteSql;
        private readonly Func<T, object> _identity;
        private readonly string _loadArraySql;
        private readonly string _loaderSql;
        private readonly IDocumentMapping _mapping;

        public Resolver(IDocumentMapping mapping)
        {
            _mapping = mapping;
            IdType = TypeMappings.ToDbType(mapping.IdMember.GetMemberType());


            _loaderSql =
                $"select {_mapping.SelectFields().Join(", ")} from {_mapping.Table.QualifiedName} as d where id = :id";
            _deleteSql = $"delete from {_mapping.Table.QualifiedName} where id = :id";
            _loadArraySql =
                $"select {_mapping.SelectFields().Join(", ")} from {_mapping.Table.QualifiedName} as d where id = ANY(:ids)";


            _identity = LambdaBuilder.Getter<T, object>(mapping.IdMember);
        }

        public Type DocumentType => _mapping.DocumentType;
        public NpgsqlDbType IdType { get; }


        public virtual T Resolve(int startingIndex, DbDataReader reader, IIdentityMap map)
        {
            if (reader.IsDBNull(startingIndex)) return null;

            var json = reader.GetString(startingIndex);
            var id = reader[startingIndex + 1];

            return map.Get<T>(id, json);
        }

        public virtual async Task<T> ResolveAsync(int startingIndex, DbDataReader reader, IIdentityMap map,
            CancellationToken token)
        {
            if (await reader.IsDBNullAsync(startingIndex, token).ConfigureAwait(false)) return null;

            var json = await reader.GetFieldValueAsync<string>(startingIndex, token).ConfigureAwait(false);

            var id = await reader.GetFieldValueAsync<object>(startingIndex + 1, token).ConfigureAwait(false);

            return map.Get<T>(id, json);
        }

        public virtual T Build(DbDataReader reader, ISerializer serializer)
        {
            return serializer.FromJson<T>(reader.GetString(0));
        }

        public T Resolve(IIdentityMap map, ILoader loader, object id)
        {
            return map.Get(id, () => loader.LoadDocument<T>(id));
        }

        public Task<T> ResolveAsync(IIdentityMap map, ILoader loader, CancellationToken token, object id)
        {
            return map.GetAsync(id, tk => loader.LoadDocumentAsync<T>(id, tk), token).ContinueWith(x => x.Result, token);
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


        /*

                public void RegisterUpdate(UpdateBatch batch, object entity)
                {
                    throw new NotImplementedException();
                }

                public void RegisterUpdate(UpdateBatch batch, object entity, string json)
                {
                    throw new NotImplementedException();
                }

    */

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