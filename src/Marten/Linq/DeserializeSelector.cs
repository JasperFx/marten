using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
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

        public T Resolve(DbDataReader reader, IIdentityMap map, QueryStatistics stats)
        {
            return _serializer.FromJson<T>(reader.GetTextReader(0));
        }

        public async Task<T> ResolveAsync(DbDataReader reader, IIdentityMap map, QueryStatistics stats, CancellationToken token)
        {
            // TODO -- replace when Npgsql has a GetTextReaderAsync()

            if (!typeof(T).IsSimple())
            {
                return _serializer.FromJson<T>(reader.GetTextReader(0));
            }


            return await reader.GetFieldValueAsync<T>(0, token).ConfigureAwait(false);
        }
    }
}