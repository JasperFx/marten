using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;

namespace Marten.Linq
{
    public class NullableScalarSelector<T> : BasicSelector, ISelector<T?> where T : struct
    {
        public T? Resolve(DbDataReader reader, IIdentityMap map, QueryStatistics stats)
        {
            return reader.IsDBNull(0) ? null : (T?)reader.GetFieldValue<T>(0);
        }

        public async Task<T?> ResolveAsync(DbDataReader reader, IIdentityMap map, QueryStatistics stats, CancellationToken token)
        {
            return await reader.IsDBNullAsync(0, token).ConfigureAwait(false) 
                ? null :
                (T?)(await reader.GetFieldValueAsync<T>(0, token).ConfigureAwait(false));
        }
    }
}