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
            using var json = reader.GetStream(0);
            return _serializer.FromJson<T>(json);
        }

        public Task<T> ResolveAsync(DbDataReader reader, CancellationToken token)
        {
            var doc = Resolve(reader);
            return Task.FromResult(doc);
        }
    }
}
