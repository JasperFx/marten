using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq.Model;
using Marten.Services;
using Npgsql;

namespace Marten.Linq.QueryHandlers
{
    public class AnyQueryHandler : IQueryHandler<bool>
    {
        private readonly ILinqQuery _query;

        public AnyQueryHandler(ILinqQuery query)
        {
            _query = query;
        }

        public Type SourceType => _query.SourceType;

        public void ConfigureCommand(NpgsqlCommand command)
        {
            _query.ConfigureAny(command);
        }

        public bool Handle(DbDataReader reader, IIdentityMap map)
        {
            if (!reader.Read())
                return false;

            return !reader.IsDBNull(0) && reader.GetBoolean(0);
        }

        public async Task<bool> HandleAsync(DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            var hasRow = await reader.ReadAsync(token).ConfigureAwait(false);


            return hasRow && !await reader.IsDBNullAsync(0, token).ConfigureAwait(false) &&
                   await reader.GetFieldValueAsync<bool>(0, token).ConfigureAwait(false);
        }
    }
}