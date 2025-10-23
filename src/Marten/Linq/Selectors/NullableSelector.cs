using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Linq.Selectors;

internal sealed class NullableSelector<T>(ISelector<T> inner): ISelector<T?>
{
    public T? Resolve(DbDataReader reader) => reader.IsDBNull(0) ? default : inner.Resolve(reader);

    public async Task<T?> ResolveAsync(DbDataReader reader, CancellationToken token)
    {
        if (await reader.IsDBNullAsync(0, token).ConfigureAwait(false)) return default;
        return await inner.ResolveAsync(reader, token).ConfigureAwait(false);
    }
}
