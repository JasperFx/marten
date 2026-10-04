#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Exceptions;
using Npgsql;

namespace Marten.Internal.Sessions;

// TODO -- look at how much more could be put here
internal class ConnectionLifetimeBase
{
    public IMartenSessionLogger Logger { get; set; } = new NulloMartenLogger();

    /// <summary>
    ///     The first failure that made this lifetime unusable, or null while it is still healthy.
    /// </summary>
    private Exception? _unusableBecause;

    /// <summary>
    ///     Whether this lifetime is currently inside a transaction whose failure would outlive the call that
    ///     caused it. False for <see cref="AutoClosingLifetime" />, which opens and closes per operation and
    ///     so has nothing to poison.
    /// </summary>
    protected virtual bool HasSurvivingTransaction => false;

    /// <summary>
    ///     #5578 / #5583. Records -- and then enforces -- that a failure inside an open transaction has made
    ///     this lifetime unusable.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Any statement that fails inside a transaction block aborts it, and PostgreSQL then answers
    ///         <c>25P02</c> to everything until the block ends. That 25P02 is only ever the consequence; the
    ///         first failure is the cause, and it is the one worth reporting.
    ///     </para>
    ///     <para>
    ///         Shared by every lifetime that carries a transaction across operations. For
    ///         <see cref="AmbientTransactionLifetime" /> and <see cref="ExternalTransaction" /> the caller
    ///         owns the transaction, so Marten deliberately does not roll anything back here -- it only stops
    ///         pretending the session still works, and names what actually went wrong.
    ///     </para>
    /// </remarks>
    protected void noteTransactionFailure(Exception e)
    {
        if (!HasSurvivingTransaction) return;

        if (_unusableBecause != null && isInFailedTransaction(e))
        {
            throw new SessionTransactionUnusableException(
                "an earlier failure aborted its transaction, and PostgreSQL has been refusing every command since.",
                _unusableBecause);
        }

        _unusableBecause ??= e;
    }

    /// <summary>The recorded cause, for a lifetime that needs to report it from somewhere else.</summary>
    protected Exception? UnusableBecause => _unusableBecause;

    protected static bool isInFailedTransaction(Exception exception)
    {
        for (var e = exception; e != null; e = e.InnerException)
        {
            if (e is PostgresException { SqlState: PostgresErrorCodes.InFailedSqlTransaction }) return true;
        }

        return false;
    }

    protected void handleCommandException(NpgsqlCommand cmd, Exception e)
    {
        Logger.LogFailure(cmd, e);

        MartenExceptionTransformer.WrapAndThrow(cmd, e);
    }

    protected void handleCommandException(NpgsqlBatch batch, Exception e)
    {
        Logger.LogFailure(batch, e);

        MartenExceptionTransformer.WrapAndThrow(batch, e);
    }
}

public interface ITransactionStarter
{
    IAlwaysConnectedLifetime Start();
    Task<IAlwaysConnectedLifetime> StartAsync(CancellationToken token);
}

public interface IAlwaysConnectedLifetime : IConnectionLifetime
{
    NpgsqlConnection Connection { get; }

    void BeginTransaction();
    ValueTask BeginTransactionAsync(CancellationToken token);
}


public interface IConnectionLifetime: IAsyncDisposable, IDisposable
{
    IMartenSessionLogger Logger { get; set; }
    int CommandTimeout { get; }

    int Execute(NpgsqlCommand cmd);
    Task<int> ExecuteAsync(NpgsqlCommand command, CancellationToken token = new());

    DbDataReader ExecuteReader(NpgsqlCommand command);

    Task<DbDataReader> ExecuteReaderAsync(NpgsqlCommand command,
        CancellationToken token = default);

    DbDataReader ExecuteReader(NpgsqlBatch batch);

    Task<DbDataReader> ExecuteReaderAsync(NpgsqlBatch batch,
        CancellationToken token = default);

    Task ExecuteBatchPagesAsync(IReadOnlyList<OperationPage> pages,
        List<Exception> exceptions, CancellationToken token,
        IReadOnlyList<ITransactionParticipant>? participants = null);
}


