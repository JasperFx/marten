using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Services;
using Npgsql;

namespace Marten.Schema
{
    public class HierarchicalDocumentStorage<T> : DocumentStorage<T> where T : class
    {
        private readonly DocumentMapping _hierarchy;

        public HierarchicalDocumentStorage(ISerializer serializer, DocumentMapping hierarchy)
            : base(serializer, hierarchy)
        {
            _hierarchy = hierarchy;
        }

        public override T Resolve(int startingIndex, DbDataReader reader, IIdentityMap map)
        {
            var id = reader[startingIndex + 1];
            var typeAlias = reader.GetString(startingIndex + 2);

            var version = reader.GetFieldValue<Guid>(3);
            
            var json = reader.GetTextReader(startingIndex);

            return map.Get<T>(id, _hierarchy.TypeFor(typeAlias), json, version);
        }

        public override async Task<T> ResolveAsync(int startingIndex, DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            if (await reader.IsDBNullAsync(startingIndex, token).ConfigureAwait(false)) return null;

            var id = await reader.GetFieldValueAsync<object>(startingIndex + 1, token).ConfigureAwait(false);

            var typeAlias = await reader.GetFieldValueAsync<string>(startingIndex + 2, token).ConfigureAwait(false);

            var version = await reader.GetFieldValueAsync<Guid>(3, token).ConfigureAwait(false);
            
            var json = await reader.As<NpgsqlDataReader>().GetTextReaderAsync(startingIndex).ConfigureAwait(false);

            return map.Get<T>(id, _hierarchy.TypeFor(typeAlias), json, version);
        }


        public override T Fetch(object id, DbDataReader reader, IIdentityMap map)
        {
            if (!reader.Read()) return null;

            var typeAlias = reader.GetString(2);

            var actualType = _hierarchy.TypeFor(typeAlias);

            var version = reader.GetFieldValue<Guid>(3);
            
            var json = reader.GetTextReader(0);

            return map.Get<T>(id, actualType, json, version);
        }

        public override async Task<T> FetchAsync(object id, DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            var found = await reader.ReadAsync(token).ConfigureAwait(false);

            if (!found) return null;

            var typeAlias = await reader.GetFieldValueAsync<string>(2, token).ConfigureAwait(false);

            var actualType = _hierarchy.TypeFor(typeAlias);

            var version = await reader.GetFieldValueAsync<Guid>(3, token).ConfigureAwait(false);
            
            var json = await reader.As<NpgsqlDataReader>().GetTextReaderAsync(0).ConfigureAwait(false);

            return map.Get<T>(id, actualType, json, version);
        }
    }
}