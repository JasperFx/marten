using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq.Model;
using Marten.Services;
using Npgsql;

namespace Marten.Linq.QueryHandlers
{
    public class CountQueryHandler<T> : IQueryHandler<T>
    {
        private readonly ILinqQuery _query;

        public CountQueryHandler(ILinqQuery query)
        {
            _query = query;
        }

        public Type SourceType => _query.SourceType;

        public void ConfigureCommand(NpgsqlCommand command)
        {
            _query.ConfigureCount(command);
        }

        public T Handle(DbDataReader reader, IIdentityMap map, QueryStatistics stats)
        {
            var hasNext = reader.Read();
            return hasNext && !reader.IsDBNull(0)
                ? reader.GetFieldValue<T>(0)
                : default(T);
        }

        public async Task<T> HandleAsync(DbDataReader reader, IIdentityMap map, QueryStatistics stats, CancellationToken token)
        {
            var hasNext = await reader.ReadAsync(token).ConfigureAwait(false);
            return hasNext && !await reader.IsDBNullAsync(0, token).ConfigureAwait(false) 
                ? await reader.GetFieldValueAsync<T>(0, token).ConfigureAwait(false) 
                : default(T);
        }
    }
}
