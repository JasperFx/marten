#nullable enable
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Linq.Selectors;

internal class TimeSpanSelector: ISelector<TimeSpan>
{
    public TimeSpan Resolve(DbDataReader reader)
    {
        var text = reader.GetString(0);
        return TimeSpan.Parse(text);
    }

    public async Task<TimeSpan> ResolveAsync(DbDataReader reader, CancellationToken token)
    {
        var text = await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false);
        return TimeSpan.Parse(text);
    }
}
