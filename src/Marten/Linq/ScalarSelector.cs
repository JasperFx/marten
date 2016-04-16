using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Services;

namespace Marten.Linq
{
    public class ScalarSelector<TResult> : BasicSelector, ISelector<TResult>
    {
        public TResult Resolve(DbDataReader reader, IIdentityMap map)
        {
            return reader.GetFieldValue<TResult>(0);
        }

        public async Task<TResult> ResolveAsync(DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            return await reader.GetFieldValueAsync<TResult>(0, token).ConfigureAwait(false);
        }
    }
}