using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Services;

namespace Marten.Linq
{
    public class DeserializeSelector<T> : ISelector<T>
    {
        private readonly ISerializer _serializer;

        public DeserializeSelector(ISerializer serializer)
        {
            _serializer = serializer;
        }

        public T Resolve(DbDataReader reader, IIdentityMap map)
        {
            return _serializer.FromJson<T>(reader.GetString(0));
        }

        public async Task<T> ResolveAsync(DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            return _serializer.FromJson<T>(await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false));
        }

        public string SelectClause(IDocumentMapping mapping)
        {
            throw new NotSupportedException();
        }
    }
}