using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Linq.Selectors;

internal sealed class NullableSelector<T>(ISelector<T> inner): ISelector<T?>
    where T : struct
{
    public T? Resolve(DbDataReader reader)
    {
        if (reader.IsDBNull(0)) return null;
        return inner.Resolve(reader);
    }

    public async Task<T?> ResolveAsync(DbDataReader reader, CancellationToken token)
    {
        if (await reader.IsDBNullAsync(0, token).ConfigureAwait(false)) return null;
        return await inner.ResolveAsync(reader, token).ConfigureAwait(false);
    }
}
