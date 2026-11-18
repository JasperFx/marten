#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Exceptions;
using Marten.Exceptions;
using Marten.Services;
using Npgsql;

namespace Marten.Internal.Sessions;

// TODO -- look at how much more could be put here
internal class ConnectionLifetimeBase
{
    public IMartenSessionLogger Logger { get; set; } = new NulloMartenLogger();

    /// <summary>
    ///     #5682: the store's application-registered exception transforms, from the <see cref="SessionOptions" />
    ///     this lifetime was built from.
    /// </summary>
    internal ExceptionTransforms? ExceptionTransforms { get; set; }

    /// <summary>
    ///     Translate a batch failure: the failing operations' own transforms, then the application's, then
    ///     Marten's built-ins.
    /// </summary>
    protected void transformAndThrow(IEnumerable<IExceptionTransform> operationTransforms, Exception e)
        => MartenExceptionTransformer.InOrder(ExceptionTransforms, operationTransforms).TransformAndThrow(e);

    /// <summary>
    ///     #5914: translate a failure of a unit of work with the transform of the operation whose statement
    ///     raised it -- see <see cref="OperationPage.TransformsFor" /> -- then the application's, then Marten's.
    /// </summary>
    protected void transformAndThrow(IReadOnlyList<OperationPage> pages, Exception e)
        => transformAndThrow(OperationPage.TransformsFor(pages, e), e);

    /// <summary>
    ///     #5914: open the reader over one page's batch. A failure is translated by the operations whose
    ///     statements the reader executed (<see cref="OperationPage.TryTransformExecuteFailure" />) and by nothing
    ///     else here; the caller's <see cref="transformAndThrow(IReadOnlyList{OperationPage}, Exception)" />
    ///     then applies the application's and Marten's transforms.
    /// </summary>
    /// <remarks>
    ///     The lifetimes that keep a connection open used to go through their own
    ///     <c>ExecuteReaderAsync(NpgsqlBatch)</c>, whose <c>handleCommandException</c> runs the application's
    ///     and Marten's built-in transforms -- so a statement that failed before the first result set was
    ///     translated before ANY operation was offered it, the reverse of the order
    ///     <see cref="MartenExceptionTransformer.InOrder" /> documents (#5682), and Marten's last built-in had
    ///     already wrapped it in a <see cref="MartenCommandException" />. <see cref="AutoClosingLifetime" />
    ///     never did that. The caller must have applied the connection and transaction.
    /// </remarks>
    protected async Task<DbDataReader> executePageReaderAsync(OperationPage page, NpgsqlBatch batch,
        CancellationToken token)
    {
        Logger.OnBeforeExecute(batch);

        try
        {
            var reader = await batch.ExecuteReaderAsync(token).ConfigureAwait(false);
            Logger.LogSuccess(batch);
            return reader;
        }
        catch (Exception e)
        {
            noteTransactionFailure(e);
            Logger.LogFailure(batch, e);

            // The SQL has to survive to the MartenCommandException the outer translation may build
            e.RecordNpgsqlBatch(batch);

            if (page.TryTransformExecuteFailure(e, out var transformed))
            {
                throw transformed;
            }

            throw;
        }
    }

    /// <summary>
    ///     The first failure that made this lifetime unusable, or null while it is still healthy.
    /// </summary>
    private Exception? _unusableBecause;

    /// <summary>
    ///     Whether this lifetime is currently inside a transaction whose failure would outlive the call that
    ///     caused it. False for <see cref="AutoClosingLifetime" />, which opens and closes per operation and
    ///     so has nothing to poison.
    /// </summary>
    protected internal virtual bool HasSurvivingTransaction => false;

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

        MartenExceptionTransformer.WrapAndThrow(ExceptionTransforms, cmd, e);
    }

    protected void handleCommandException(NpgsqlBatch batch, Exception e)
    {
        Logger.LogFailure(batch, e);

        MartenExceptionTransformer.WrapAndThrow(ExceptionTransforms, batch, e);
    }
}

internal static class ConnectionLifetimeExtensions
{
    /// <summary>
    ///     Whether this lifetime is inside a transaction that outlives the current call, so that work done
    ///     through it is not yet committed when the call returns.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="ConnectionLifetimeBase.HasSurvivingTransaction" /> answers this with the right
    ///         precision per lifetime. This wrapper exists because <see cref="EventTracingConnectionLifetime" />
    ///         decorates an inner lifetime without deriving from the base, so asking the outer object directly
    ///         would answer <c>false</c> for a session that is inside a transaction whenever OpenTelemetry
    ///         tracing is on.
    ///     </para>
    ///     <para>
    ///         An implementation Marten does not recognise answers <c>false</c>. Every lifetime Marten itself
    ///         builds is covered by one of the two branches below.
    ///     </para>
    /// </remarks>
    internal static bool HasSurvivingTransaction(this IConnectionLifetime lifetime)
    {
        return lifetime switch
        {
            EventTracingConnectionLifetime tracing => tracing.InnerConnectionLifetime.HasSurvivingTransaction(),
            ConnectionLifetimeBase lifetimeBase => lifetimeBase.HasSurvivingTransaction,
            _ => false
        };
    }
}

public interface ITransactionStarter
{
    Task<IAlwaysConnectedLifetime> StartAsync(CancellationToken token);
}

/// <remarks>
///     Marten 10 (#5861): the synchronous <c>Connection</c> getter, which opened the connection on first use,
///     and <c>BeginTransaction()</c> are gone. Use <see cref="OpenConnectionAsync" /> and
///     <see cref="BeginTransactionAsync" />.
/// </remarks>
public interface IAlwaysConnectedLifetime : IConnectionLifetime
{
    /// <summary>
    ///     The lifetime's connection, opening it first if it is not open yet. The lifetime owns it.
    /// </summary>
    ValueTask<NpgsqlConnection> OpenConnectionAsync(CancellationToken token);

    ValueTask BeginTransactionAsync(CancellationToken token);
}


public interface IConnectionLifetime: IAsyncDisposable, IDisposable
{
    IMartenSessionLogger Logger { get; set; }
    int CommandTimeout { get; }

    Task<int> ExecuteAsync(NpgsqlCommand command, CancellationToken token = new());

    Task<DbDataReader> ExecuteReaderAsync(NpgsqlCommand command,
        CancellationToken token = default);

    Task<DbDataReader> ExecuteReaderAsync(NpgsqlBatch batch,
        CancellationToken token = default);

    Task ExecuteBatchPagesAsync(IReadOnlyList<OperationPage> pages,
        List<Exception> exceptions, CancellationToken token,
        IReadOnlyList<ITransactionParticipant>? participants = null);
}


