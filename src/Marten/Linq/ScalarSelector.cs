using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;

namespace Marten.Linq
{
    public class ScalarSelector<TResult> : BasicSelector, ISelector<TResult>
    {
        private static readonly string NullResultMessage = $"The cast to value type '{typeof(TResult).FullName}' failed because the materialized value is null. Either the result type's generic parameter or the query must use a nullable type.";

        public TResult Resolve(DbDataReader reader, IIdentityMap map, QueryStatistics stats)
        {
            try
            {
                return reader.GetFieldValue<TResult>(0);
            }
            catch (InvalidCastException e)
            {
                throw new InvalidOperationException(NullResultMessage, e);
            }
        }

        public Task<TResult> ResolveAsync(DbDataReader reader, IIdentityMap map, QueryStatistics stats, CancellationToken token)
        {
            return reader.GetFieldValueAsync<TResult>(0, token);
        }
    }
}