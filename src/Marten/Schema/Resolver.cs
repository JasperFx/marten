using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;

namespace Marten.Schema
{
    public class Resolver<T> : IResolver<T> where T : class
    {
        private readonly IDocumentMapping _mapping;

        public Resolver(IDocumentMapping mapping)
        {
            _mapping = mapping;
        }

        // DocumentStorage methods


        public virtual T Resolve(int startingIndex, DbDataReader reader, IIdentityMap map)
        {
            if (reader.IsDBNull(startingIndex)) return null;

            var json = reader.GetString(startingIndex);
            var id = reader[startingIndex + 1];

            return map.Get <T> (id, json);
        }

        public virtual async Task<T> ResolveAsync(int startingIndex, DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            if (await reader.IsDBNullAsync(startingIndex, token).ConfigureAwait(false)) return null;

            var json = await reader.GetFieldValueAsync<string>(startingIndex, token).ConfigureAwait(false);

            var id = await reader.GetFieldValueAsync<object>(startingIndex + 1, token).ConfigureAwait(false);

            return map.Get<T>(id, json);
        }

        public virtual T Build(DbDataReader reader, ISerializer serializer)
        {
            return serializer.FromJson <T> (reader.GetString(0));
        }

        public T Resolve(IIdentityMap map, ILoader loader, object id)
        {
            return map.Get(id, () => loader.LoadDocument<T>(id));
        }

        public Task<T> ResolveAsync(IIdentityMap map, ILoader loader, CancellationToken token, object id)
        {
            return map.GetAsync(id, (tk => loader.LoadDocumentAsync <T>(id, tk)), token).ContinueWith(x => x.Result as T, token);
        }
    }
}