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
        private static readonly string NullResultMessage = $"The cast to value type '{typeof(TResult).FullName}' failed because the materialized value is null. Either the result type's generic parameter or the query must use a nullable type.";

        public TResult Resolve(DbDataReader reader, IIdentityMap map)
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

        public Task<TResult> ResolveAsync(DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            return reader.GetFieldValueAsync<TResult>(0, token);
        }
    }
}