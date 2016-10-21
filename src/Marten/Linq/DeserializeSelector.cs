using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;

namespace Marten.Linq
{
    public class DeserializeSelector<T> : BasicSelector, ISelector<T>
    {
        private readonly ISerializer _serializer;

        public DeserializeSelector(ISerializer serializer) : base("data")
        {
            _serializer = serializer;
        }

        public DeserializeSelector(ISerializer serializer, params string[] selectFields) : base(selectFields)
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
    }
}