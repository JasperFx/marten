using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Baseline.Conversion;
using Marten.Linq.Fields;
using Marten.Schema;
using Marten.Services;

namespace Marten.Linq
{
    public class ArrayElementFieldSelector<T>: BasicSelector, ISelector<T>
    {
        private readonly Func<string, object> _converter;

        public ArrayElementFieldSelector(bool distinct, IField field, Conversions conversions) : base(distinct, $"jsonb_array_elements_text({field.JSONBLocator})")
        {
            _converter = conversions.FindConverter(typeof(T));
        }

        public T Resolve(DbDataReader reader, IIdentityMap map, QueryStatistics stats)
        {
            if (reader.IsDBNull(0))
                return default(T);

            var raw = reader.GetString(0);
            return (T)_converter(raw);
        }

        public async Task<T> ResolveAsync(DbDataReader reader, IIdentityMap map, QueryStatistics stats, CancellationToken token)
        {
            var isNull = await reader.IsDBNullAsync(0, token).ConfigureAwait(false);

            if (isNull)
                return default(T);

            var raw = await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false);

            return (T)_converter(raw);
        }
    }
}
