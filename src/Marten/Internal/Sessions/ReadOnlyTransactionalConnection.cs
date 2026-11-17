using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;
using Npgsql;
using Weasel.Core;

namespace Marten.Internal.Sessions;

internal class ReadOnlyTransactionalConnection: TransactionalConnection
{
    private const string SetTransactionReadOnly = "SET TRANSACTION READ ONLY;";

    public ReadOnlyTransactionalConnection(SessionOptions options): base(options)
    {
    }

    public override async Task ApplyAsync(NpgsqlCommand command, CancellationToken token)
    {
        await EnsureConnectedAsync(token).ConfigureAwait(false);

        command.Connection = _connection;
        command.Transaction = Transaction;
        command.CommandTimeout = CommandTimeout;
    }

    public override async ValueTask BeginTransactionAsync(CancellationToken token)
    {
        if (Transaction == null)
        {
            await base.BeginTransactionAsync(token).ConfigureAwait(false);

            try
            {
                await using var command = Transaction!.CreateCommand(SetTransactionReadOnly);
                await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // #5881: the transaction exists but was never made read only. Left in place, the next call
                // would find a transaction and skip this method's work, and the session would carry on in a
                // writable one.
                var transaction = Transaction;
                Transaction = null;

                try
                {
                    await transaction!.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Logger.LogFailure(e, "Failure trying to dispose a transaction that could not be made read only");
                }

                throw;
            }
        }
    }
}
