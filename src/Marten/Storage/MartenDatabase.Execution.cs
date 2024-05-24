using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;
using Npgsql;

namespace Marten.Storage;

public partial class MartenDatabase : ISingleQueryRunner
{
    internal record SingleQuery<T>(ISingleQueryHandler<T> Handler, MartenDatabase Database)
    {
        public async Task<T> ExecuteAsync(CancellationToken cancellation)
        {
            await using var conn = Database.CreateConnection();

            var command = Handler.BuildCommand();
            try
            {
                command.Connection = conn;
            }
            catch (InvalidOperationException e)
            {
                Console.WriteLine(e);
                throw;
            }

            await conn.OpenAsync(cancellation).ConfigureAwait(false);
            await using var reader = await command.ExecuteReaderAsync(cancellation).ConfigureAwait(false);

            try
            {
                return await Handler.HandleAsync(reader, cancellation).ConfigureAwait(false);
            }
            catch (InvalidOperationException e)
            {
                throw;
            }
            finally
            {
                try
                {
                    await reader.CloseAsync().ConfigureAwait(false);
                    await reader.DisposeAsync().ConfigureAwait(false);
                    await conn.CloseAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // don't let anything escape here
                }
            }
        }
    }

    public Task<T> Query<T>(ISingleQueryHandler<T> handler, CancellationToken cancellation)
    {
        return Options.ResiliencePipeline.ExecuteAsync<T, SingleQuery<T>>(
            static (query, t) => new ValueTask<T>(query.ExecuteAsync(t)), new SingleQuery<T>(handler, this), cancellation).AsTask();
    }

    public async Task SingleCommit(DbCommand command, CancellationToken cancellation)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(cancellation).ConfigureAwait(false);

        command.Connection = conn;

        await command.ExecuteNonQueryAsync(cancellation).ConfigureAwait(false);
    }

    public NpgsqlConnection CreateConnection(ConnectionUsage connectionUsage = ConnectionUsage.ReadWrite)
    {
        if (connectionUsage == ConnectionUsage.Read)
        {
            return CreateConnection(Options.Advanced.MultiHostSettings.ReadSessionPreference);
        }

        return CreateConnection(Options.Advanced.MultiHostSettings.WriteSessionPreference);
    }

}
