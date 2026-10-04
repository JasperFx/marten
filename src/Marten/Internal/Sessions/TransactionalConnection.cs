#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Exceptions;
using Marten.Exceptions;
using Marten.Services;
using Npgsql;

namespace Marten.Internal.Sessions;

internal class TransactionalConnection: ConnectionLifetimeBase, IAlwaysConnectedLifetime
{
    protected readonly SessionOptions _options;
    protected NpgsqlConnection? _connection;

    /// <summary>
    ///     The first failure that made this lifetime unusable, or null while it is still healthy. See
    ///     <see cref="SessionTransactionUnusableException" /> for why a sticky lifetime needs this at all:
    ///     unlike <see cref="AutoClosingLifetime" />, it carries one connection and one transaction across every
    ///     operation in the session, so a failure that kills either of them is not confined to the call that
    ///     provoked it.
    /// </summary>
    private Exception? _unusableBecause;

    public TransactionalConnection(SessionOptions options)
    {
        _options = options;
        _connection = _options.Connection;

        CommandTimeout = _options.Timeout ?? _connection?.CommandTimeout ?? 30;
    }

    public NpgsqlConnection Connection
    {
        get
        {
            EnsureConnected();
            return _connection!;
        }
    }

    public int CommandTimeout { get; set; }

    public NpgsqlTransaction? Transaction { get; protected set; }

    public async ValueTask DisposeAsync()
    {
        if (Transaction != null)
        {
            await Transaction.DisposeAsync().ConfigureAwait(false);
        }

        if (_connection is { State: ConnectionState.Open })
        {
            await BeforeCloseAsync(_connection, CancellationToken.None).ConfigureAwait(false);
        }

        if (_connection != null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        Transaction?.SafeDispose();
        if (_connection is { State: ConnectionState.Open })
        {
            BeforeClose(_connection);
        }
        _connection?.SafeDispose();
    }

    public virtual void Apply(NpgsqlCommand command)
    {
        EnsureConnected();

        command.Connection = _connection;
        command.Transaction = Transaction;
        command.CommandTimeout = CommandTimeout;
    }

    public void Apply(NpgsqlBatch batch)
    {
        EnsureConnected();

        batch.Connection = _connection;
        batch.Transaction = Transaction;
        batch.Timeout = CommandTimeout;
    }

    public virtual void BeginTransaction()
    {
        EnsureConnected();
        if (Transaction == null)
        {
            Transaction = _connection.BeginTransaction(_options.IsolationLevel);
        }
    }

    // TODO -- this should be ValueTask
    public virtual async Task ApplyAsync(NpgsqlCommand command, CancellationToken token)
    {
        await EnsureConnectedAsync(token).ConfigureAwait(false);

        command.Connection = _connection;
        command.Transaction = Transaction;
        command.CommandTimeout = CommandTimeout;
    }

    public async Task ApplyAsync(NpgsqlBatch batch, CancellationToken token)
    {
        await EnsureConnectedAsync(token).ConfigureAwait(false);

        batch.Connection = _connection;
        batch.Transaction = Transaction;
        batch.Timeout = CommandTimeout;
    }

    public virtual async ValueTask BeginTransactionAsync(CancellationToken token)
    {
        await EnsureConnectedAsync(token).ConfigureAwait(false);
        Transaction ??= await _connection
            .BeginTransactionAsync(_options.IsolationLevel, token).ConfigureAwait(false);
    }

    public void Commit()
    {
        if (Transaction == null)
        {
            throw new InvalidOperationException("Trying to commit a transaction that was never started");
        }

        Transaction.Commit();
        Transaction.Dispose();
        Transaction = null;

        if (_connection is { State: ConnectionState.Open })
        {
            BeforeClose(_connection);
        }
        _connection?.Close();
        _connection = null;
    }

    public async Task CommitAsync(CancellationToken token)
    {
        if (Transaction == null)
        {
            throw new InvalidOperationException("Trying to commit a transaction that was never started");
        }

        await Transaction.CommitAsync(token).ConfigureAwait(false);
        await Transaction.DisposeAsync().ConfigureAwait(false);
        Transaction = null;

        if (_connection != null)
        {
            if (_connection.State == ConnectionState.Open)
            {
                await BeforeCloseAsync(_connection, token).ConfigureAwait(false);
            }
            await _connection.CloseAsync().ConfigureAwait(false);
            await _connection.DisposeAsync().ConfigureAwait(false);
        }

        _connection = null;
    }

    public void Rollback()
    {
        if (Transaction != null)
        {
            Transaction.Rollback();
            Transaction.Dispose();
            Transaction = null;

            if (_connection is { State: ConnectionState.Open })
            {
                BeforeClose(_connection);
            }
            _connection?.Close();
            _connection?.Dispose();
            _connection = null;
        }
    }

    public async Task RollbackAsync(CancellationToken token)
    {
        if (Transaction != null)
        {
            await Transaction.RollbackAsync(token).ConfigureAwait(false);
            await Transaction.DisposeAsync().ConfigureAwait(false);
            Transaction = null;

            if (_connection != null)
            {
                if (_connection.State == ConnectionState.Open)
                {
                    await BeforeCloseAsync(_connection, token).ConfigureAwait(false);
                }
                await _connection.CloseAsync().ConfigureAwait(false);
                await _connection.DisposeAsync().ConfigureAwait(false);
            }

            _connection = null;
        }
    }


    public void EnsureConnected()
    {
        if (_connection == null)
        {
#pragma warning disable CS8602
            _connection = _options.Tenant.Database.CreateConnection();
#pragma warning restore CS8602
        }
        else if (discardBrokenConnection())
        {
            _connection.SafeDispose();
#pragma warning disable CS8602
            _connection = _options.Tenant.Database.CreateConnection();
#pragma warning restore CS8602
        }

        if (_connection.State == ConnectionState.Closed)
        {
            _connection.Open();
            AfterOpened(_connection);
        }
    }

    public async ValueTask EnsureConnectedAsync(CancellationToken token)
    {
        if (_connection == null)
        {
#pragma warning disable CS8602
            _connection = _options.Tenant.Database.CreateConnection();
#pragma warning restore CS8602
        }
        else if (discardBrokenConnection())
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
#pragma warning disable CS8602
            _connection = _options.Tenant.Database.CreateConnection();
#pragma warning restore CS8602
        }

        if (_connection.State == ConnectionState.Closed)
        {
            await _connection.OpenAsync(token).ConfigureAwait(false);
            await AfterOpenedAsync(_connection, token).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     #5577. Whether the current connection is broken and may be replaced with a fresh one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Npgsql reports a broken connection as <see cref="ConnectionState.Closed" /> -- the break shows only
    ///         in <see cref="NpgsqlConnection.FullState" /> -- so testing <c>State</c> alone made the reopen below
    ///         look like the ordinary "not connected yet" path. It is not. Reopening acquires a <i>different</i>
    ///         backend, and <see cref="Apply(NpgsqlCommand)" /> then hands the command the stale
    ///         <see cref="NpgsqlTransaction" />, which Npgsql accepts without complaint. The command runs with no
    ///         transaction at all, and a <c>Serializable</c> session quietly starts answering from a newer
    ///         snapshot than the one it was opened to hold.
    ///     </para>
    ///     <para>
    ///         So: with no transaction open there is nothing to lose and a fresh connection is strictly better
    ///         than calling <c>Open()</c> on a broken object. With one open, the transaction died with the backend
    ///         and no connection can bring it back, so the session fails instead of pretending otherwise.
    ///     </para>
    /// </remarks>
    private bool discardBrokenConnection()
    {
        if (!_connection!.FullState.HasFlag(ConnectionState.Broken)) return false;

        if (Transaction != null)
        {
            throw new SessionTransactionUnusableException(
                "its connection was broken while a transaction was open, and that transaction cannot be resumed on a new connection.",
                _unusableBecause ?? new InvalidOperationException(
                    $"The connection to {_connection.Database} was broken."));
        }

        // A caller-supplied connection is not ours to replace.
        if (ReferenceEquals(_connection, _options.Connection))
        {
            throw new SessionTransactionUnusableException(
                "the connection supplied through SessionOptions was broken, and Marten will not silently substitute one of its own.",
                _unusableBecause ?? new InvalidOperationException(
                    $"The connection to {_connection.Database} was broken."));
        }

        return true;
    }

    /// <summary>
    ///     #5578. Records -- and then enforces -- that a failure inside an open transaction has made this
    ///     lifetime unusable. Any statement that fails inside a transaction block aborts it, so every later
    ///     command on this session is answered <c>25P02</c> by PostgreSQL regardless of what it asks for.
    ///     That 25P02 is only ever the consequence; the first failure is the cause, and it is the one worth
    ///     reporting.
    /// </summary>
    private void noteTransactionFailure(Exception e)
    {
        // AutoClosingLifetime has no surviving transaction to poison, and neither do we before one is begun.
        if (Transaction == null) return;

        if (_unusableBecause != null && isInFailedTransaction(e))
        {
            throw new SessionTransactionUnusableException(
                "an earlier failure aborted its transaction, and PostgreSQL has been refusing every command since.",
                _unusableBecause);
        }

        _unusableBecause ??= e;
    }

    private static bool isInFailedTransaction(Exception exception)
    {
        for (var e = exception; e != null; e = e.InnerException)
        {
            if (e is PostgresException { SqlState: PostgresErrorCodes.InFailedSqlTransaction }) return true;
        }

        return false;
    }

    protected virtual void AfterOpened(NpgsqlConnection connection)
    {
    }

    protected virtual Task AfterOpenedAsync(NpgsqlConnection connection, CancellationToken token)
    {
        return Task.CompletedTask;
    }

    protected virtual void BeforeClose(NpgsqlConnection connection)
    {
    }

    protected virtual Task BeforeCloseAsync(NpgsqlConnection connection, CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public int Execute(NpgsqlCommand cmd)
    {
        Apply(cmd);

        try
        {
            var returnValue = cmd.ExecuteNonQuery();
            Logger.LogSuccess(cmd);

            return returnValue;
        }
        catch (Exception e)
        {
            noteTransactionFailure(e);
            handleCommandException(cmd, e);
            throw;
        }
    }

    public async Task<int> ExecuteAsync(NpgsqlCommand command,
        CancellationToken token = new())
    {
        await ApplyAsync(command, token).ConfigureAwait(false);

        Logger.OnBeforeExecute(command);

        try
        {
            var returnValue = await command.ExecuteNonQueryAsync(token)
                .ConfigureAwait(false);
            Logger.LogSuccess(command);

            return returnValue;
        }
        catch (Exception e)
        {
            noteTransactionFailure(e);
            handleCommandException(command, e);
            throw;
        }
    }

    public DbDataReader ExecuteReader(NpgsqlCommand command)
    {
        Apply(command);

        try
        {
            var returnValue = command.ExecuteReader();
            Logger.LogSuccess(command);
            return returnValue;
        }
        catch (Exception e)
        {
            noteTransactionFailure(e);
            handleCommandException(command, e);
            throw;
        }
    }

    public async Task<DbDataReader> ExecuteReaderAsync(NpgsqlCommand command, CancellationToken token = default)
    {
        await ApplyAsync(command, token).ConfigureAwait(false);

        Logger.OnBeforeExecute(command);

        try
        {
            var reader = await command.ExecuteReaderAsync(token)
                .ConfigureAwait(false);

            Logger.LogSuccess(command);

            return reader;
        }
        catch (Exception e)
        {
            noteTransactionFailure(e);
            handleCommandException(command, e);
            throw;
        }
    }

    public DbDataReader ExecuteReader(NpgsqlBatch batch)
    {
        Apply(batch);

        try
        {
            var reader = batch.ExecuteReader();
            Logger.LogSuccess(batch);
            return reader;
        }
        catch (Exception e)
        {
            noteTransactionFailure(e);
            handleCommandException(batch, e);
            throw;
        }
    }

    public async Task<DbDataReader> ExecuteReaderAsync(NpgsqlBatch batch, CancellationToken token = default)
    {
        await ApplyAsync(batch, token).ConfigureAwait(false);

        Logger.OnBeforeExecute(batch);

        try
        {
            var reader = await batch.ExecuteReaderAsync(token)
                .ConfigureAwait(false);

            Logger.LogSuccess(batch);

            return reader;
        }
        catch (Exception e)
        {
            noteTransactionFailure(e);
            handleCommandException(batch, e);
            throw;
        }
    }

    public async Task ExecuteBatchPagesAsync(IReadOnlyList<OperationPage> pages,
        List<Exception> exceptions, CancellationToken token,
        IReadOnlyList<ITransactionParticipant>? participants = null)
    {
        try
        {
            await BeginTransactionAsync(token).ConfigureAwait(false);
            foreach (var page in pages)
            {
                var batch = page.Compile();
                await using var reader = await ExecuteReaderAsync(batch, token).ConfigureAwait(false);
                await page.ApplyCallbacksAsync(reader, exceptions, token).ConfigureAwait(false);
                await reader.CloseAsync().ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            await RollbackAsync(token).ConfigureAwait(false);
            Logger.LogFailure(new NpgsqlCommand(), e);
            pages.SelectMany(x => x.Operations).OfType<IExceptionTransform>().Concat(MartenExceptionTransformer.Transforms).TransformAndThrow(e);
        }

        if (exceptions.Count == 1)
        {
            await RollbackAsync(token).ConfigureAwait(false);
            var ex = exceptions.Single();
            ExceptionDispatchInfo.Throw(ex);
        }

        if (exceptions.Any())
        {
            await RollbackAsync(token).ConfigureAwait(false);
            throw new AggregateException(exceptions);
        }

        if (participants is { Count: > 0 })
        {
            foreach (var participant in participants)
            {
                await participant.BeforeCommitAsync(Connection, Transaction!, token).ConfigureAwait(false);
            }
        }

        await CommitAsync(token).ConfigureAwait(true);
    }
}
