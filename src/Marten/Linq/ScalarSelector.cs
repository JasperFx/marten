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
            var type = typeof (TResult);
            var result = default(TResult);
            if (reader.FieldCount == 0) return result;

            var value = reader.GetValue(0);
            return convertType(reader, type, value);
        }

        public async Task<TResult> ResolveAsync(DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            return await reader.GetFieldValueAsync<TResult>(0, token).ConfigureAwait(false);
        }

        private static TResult convertType(DbDataReader reader, Type type, object value)
        {
            if (type.IsNullable())
                return Convert.ChangeType(value, typeof (TResult).GetInnerTypeFromNullable()).As<TResult>();

            return Convert.ChangeType(reader.GetValue(0), typeof (TResult)).As<TResult>();
        }
    }
}