#nullable enable

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Exceptions;
using Marten.Exceptions;
using Marten.Services;
using Npgsql;

namespace Marten.Internal.Sessions;

internal class AmbientTransactionLifetime: ConnectionLifetimeBase, IAlwaysConnectedLifetime
{
    private readonly SessionOptions _options;
    private NpgsqlConnection? _connection;

    public AmbientTransactionLifetime(SessionOptions options)
    {
        _options = options;

        _connection = options.Connection;

        if (options.Connection != null && options.Connection.State != ConnectionState.Closed)
        {
            OwnsConnection = false;
        }
        else
        {
            OwnsConnection = true;
        }

        CommandTimeout = _options.Timeout ?? _connection?.CommandTimeout ?? StoreOptions.DefaultTimeout;
    }

    public NpgsqlConnection Connection
    {
        get
        {
            EnsureConnected();
            return _connection!;
        }
    }

    public bool OwnsConnection { get; }

    public int CommandTimeout { get; set; }




    public ValueTask DisposeAsync()
    {
        if (_connection != null)
        {
            return _connection.DisposeAsync();
        }

        return new ValueTask();
    }

    public void Dispose()
    {
        if (OwnsConnection)
        {
            _connection?.Close();
            _connection?.Dispose();
        }
    }

    public void Apply(NpgsqlCommand command)
    {
        BeginTransaction();
        command.CommandTimeout = CommandTimeout;
        command.Connection = _connection;
        command.CommandTimeout = CommandTimeout;
    }

    public async Task ApplyAsync(NpgsqlCommand command, CancellationToken token)
    {
        await BeginTransactionAsync(token).ConfigureAwait(false);
        command.Connection = _connection;
        command.CommandTimeout = CommandTimeout;
        command.CommandTimeout = CommandTimeout;
    }

    public void Apply(NpgsqlBatch batch)
    {
        BeginTransaction();
        batch.Connection = _connection;
        batch.Timeout = CommandTimeout;
    }

    public async Task ApplyAsync(NpgsqlBatch batch, CancellationToken token)
    {
        await BeginTransactionAsync(token).ConfigureAwait(false);
        batch.Connection = _connection;
        batch.Timeout = CommandTimeout;
    }

    public void BeginTransaction()
    {
        EnsureConnected();
    }

    public void EnsureConnected()
    {
        if (_connection == null)
        {
#pragma warning disable CS8602
            _connection = _options.Tenant.Database.CreateConnection();
#pragma warning restore CS8602
            _connection.Open();
            _connection.EnlistTransaction(_options.DotNetTransaction);
        }
    }

    public ValueTask EnsureConnectedAsync(CancellationToken token)
    {
        return BeginTransactionAsync(token);
    }

    public async ValueTask BeginTransactionAsync(CancellationToken token)
    {
        if (_connection == null)
        {
#pragma warning disable CS8602
            _connection = _options.Tenant.Database.CreateConnection();
#pragma warning restore CS8602
            await _connection.OpenAsync(token).ConfigureAwait(false);
            _connection.EnlistTransaction(_options.DotNetTransaction);
        }
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
            handleCommandException(batch, e);
            throw;
        }
    }

    public async Task ExecuteBatchPagesAsync(IReadOnlyList<OperationPage> pages,
        List<Exception> exceptions, CancellationToken token)
    {
        try
        {
            await BeginTransactionAsync(token).ConfigureAwait(false);
            foreach (var page in pages)
            {
                var batch = page.Compile();
                await using var reader = await ExecuteReaderAsync(batch, token).ConfigureAwait(false);
                await page.ApplyCallbacksAsync(reader, exceptions, token).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            Logger.LogFailure(new NpgsqlCommand(), e);
            pages.SelectMany(x => x.Operations).OfType<IExceptionTransform>().Concat(MartenExceptionTransformer.Transforms).TransformAndThrow(e);
        }

        if (exceptions.Count == 1)
        {
            var ex = exceptions.Single();
            ExceptionDispatchInfo.Throw(ex);
        }

        if (exceptions.Any())
        {
            throw new AggregateException(exceptions);
        }

    }
}
