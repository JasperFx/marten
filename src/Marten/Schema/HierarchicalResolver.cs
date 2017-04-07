using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;

namespace Marten.Schema
{
    public class HierarchicalResolver<T> : Resolver<T> where T : class
    {
        private readonly DocumentMapping _hierarchy;

        public HierarchicalResolver(ISerializer serializer, DocumentMapping hierarchy, bool useCharBufferPooling)
            : base(serializer, hierarchy, useCharBufferPooling)
        {
            _hierarchy = hierarchy;
        }

        public override T Resolve(int startingIndex, DbDataReader reader, IIdentityMap map)
        {
            var json = reader.GetTextReader(startingIndex);
            var id = reader[startingIndex + 1];
            var typeAlias = reader.GetString(startingIndex + 2);

            var version = reader.GetFieldValue<Guid>(3);

            return map.Get<T>(id, _hierarchy.TypeFor(typeAlias), json, version);
        }

        public override async Task<T> ResolveAsync(int startingIndex, DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            if (await reader.IsDBNullAsync(startingIndex, token).ConfigureAwait(false)) return null;

            var json = reader.GetTextReader(startingIndex);
            //var json = await reader.GetFieldValueAsync<string>(startingIndex, token).ConfigureAwait(false);

            var id = await reader.GetFieldValueAsync<object>(startingIndex + 1, token).ConfigureAwait(false);

            var typeAlias = await reader.GetFieldValueAsync<string>(startingIndex + 2, token).ConfigureAwait(false);

            var version = await reader.GetFieldValueAsync<Guid>(3, token).ConfigureAwait(false);

            return map.Get<T>(id, _hierarchy.TypeFor(typeAlias), json, version);
        }


        public override FetchResult<T> Fetch(DbDataReader reader, ISerializer serializer)
        {
            if (!reader.Read()) return null;

            var json = reader.GetString(0);
            var typeAlias = reader.GetString(2);

            var actualType = _hierarchy.TypeFor(typeAlias);

            var doc = (T)serializer.FromJson(actualType, json);

            var version = reader.GetFieldValue<Guid>(3);

            return new FetchResult<T>(doc, json, version);
        }

        public override async Task<FetchResult<T>> FetchAsync(DbDataReader reader, ISerializer serializer, CancellationToken token)
        {
            var found = await reader.ReadAsync(token).ConfigureAwait(false);

            if (!found) return null;

            var json = await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false);
            var typeAlias = await reader.GetFieldValueAsync<string>(2, token).ConfigureAwait(false);

            var actualType = _hierarchy.TypeFor(typeAlias);

            var doc = (T)serializer.FromJson(actualType, json);

            var version = await reader.GetFieldValueAsync<Guid>(3, token).ConfigureAwait(false);

            return new FetchResult<T>(doc, json, version);
        }
    }
}