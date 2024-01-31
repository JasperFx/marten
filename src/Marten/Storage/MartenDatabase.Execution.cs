using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;

namespace Marten.Storage;

public partial class MartenDatabase : ISingleQueryRunner
{
    public async Task<T> Query<T>(ISingleQueryHandler<T> handler, CancellationToken cancellation)
    {
        await using var conn = CreateConnection();

        var command = handler.BuildCommand();
        command.Connection = conn;

        await conn.OpenAsync(cancellation).ConfigureAwait(false);
        await using var reader = await command.ExecuteReaderAsync(cancellation).ConfigureAwait(false);

        try
        {
            return await handler.HandleAsync(reader, cancellation).ConfigureAwait(false);
        }
        finally
        {
            await reader.CloseAsync().ConfigureAwait(false);
            await reader.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async Task SingleCommit(DbCommand command, CancellationToken cancellation)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(cancellation).ConfigureAwait(false);

        command.Connection = conn;

        await command.ExecuteNonQueryAsync(cancellation).ConfigureAwait(false);
    }
}
