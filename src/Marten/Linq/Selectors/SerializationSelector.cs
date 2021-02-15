using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Linq.Selectors
{
    public class SerializationSelector<T>: ISelector<T>
    {
        private readonly ISerializer _serializer;

        public SerializationSelector(ISerializer serializer)
        {
            _serializer = serializer;
        }

        public T Resolve(DbDataReader reader)
        {
            return _serializer.FromJson<T>(reader, 0);
        }

        public async Task<T> ResolveAsync(DbDataReader reader, CancellationToken token)
        {
            return await _serializer.FromJsonAsync<T>(reader, 0, token);
        }
    }
}
