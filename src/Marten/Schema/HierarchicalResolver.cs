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

        public HierarchicalResolver(DocumentMapping hierarchy)
            : base(hierarchy)
        {
            _hierarchy = hierarchy;
        }

        public override T Resolve(int startingIndex, DbDataReader reader, IIdentityMap map)
        {
            var json = reader.GetString(startingIndex);
            var id = reader[startingIndex + 1];
            var typeAlias = reader.GetString(startingIndex + 2);

            return map.Get<T>(id, _hierarchy.TypeFor(typeAlias), json);
        }

        public override async Task<T> ResolveAsync(int startingIndex, DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            if (await reader.IsDBNullAsync(startingIndex, token).ConfigureAwait(false)) return null;

            var json = await reader.GetFieldValueAsync<string>(startingIndex, token).ConfigureAwait(false);

            var id = await reader.GetFieldValueAsync<object>(startingIndex + 1, token).ConfigureAwait(false);

            var typeAlias = await reader.GetFieldValueAsync<string>(startingIndex + 2, token).ConfigureAwait(false);

            return map.Get<T>(id, _hierarchy.TypeFor(typeAlias), json);
        }

        public override T Build(DbDataReader reader, ISerializer serializer)
        {
            var json = reader.GetString(0);
            var typeAlias = reader.GetString(2);

            var actualType = _hierarchy.TypeFor(typeAlias);


            return (T) serializer.FromJson(actualType, json);
        }
    }
}