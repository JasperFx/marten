using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Storage;

namespace Marten.Services
{
    internal class AutoOpenSingleQueryRunner : ISingleQueryRunner
    {
        private readonly ITenant _tenant;

        public AutoOpenSingleQueryRunner(ITenant tenant)
        {
            _tenant = tenant;
        }


        public async Task<T> Query<T>(ISingleQueryHandler<T> handler, CancellationToken cancellation)
        {
            using var conn = _tenant.CreateConnection();

            var command = handler.BuildCommand();
            command.Connection = conn;

            await conn.OpenAsync(cancellation);
            using var reader = await command.ExecuteReaderAsync(cancellation);

            return await handler.HandleAsync(reader, cancellation);
        }

        public async Task SingleCommit(DbCommand command, CancellationToken cancellation)
        {
            using var conn = _tenant.CreateConnection();
            await conn.OpenAsync(cancellation);

            command.Connection = conn;

            await command.ExecuteNonQueryAsync(cancellation);
        }
    }
}
