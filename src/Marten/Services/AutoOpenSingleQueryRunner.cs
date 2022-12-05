using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Storage;

namespace Marten.Services;

internal class AutoOpenSingleQueryRunner: ISingleQueryRunner
{
    private readonly IMartenDatabase _database;

    public AutoOpenSingleQueryRunner(IMartenDatabase database)
    {
        _database = database;
    }


    public async Task<T> Query<T>(ISingleQueryHandler<T> handler, CancellationToken cancellation)
    {
        await using var conn = _database.CreateConnection();

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
        await using var conn = _database.CreateConnection();
        await conn.OpenAsync(cancellation).ConfigureAwait(false);

        command.Connection = conn;

        await command.ExecuteNonQueryAsync(cancellation).ConfigureAwait(false);
    }
}
