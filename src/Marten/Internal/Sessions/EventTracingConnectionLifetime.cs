using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;
using Npgsql;

namespace Marten.Internal.Sessions;

internal class EventTracingConnectionLifetime : IConnectionLifetime
{
    private readonly IConnectionLifetime _innerConnectionLifetime;
    private readonly OpenTelemetryOptions _openTelemetryOptions;
    private readonly Activity _databaseActivity;

    public EventTracingConnectionLifetime(IConnectionLifetime innerConnectionLifetime, OpenTelemetryOptions openTelemetryOptions, Activity databaseActivity)
    {
        if (innerConnectionLifetime == null)
        {
            throw new ArgumentNullException(nameof(innerConnectionLifetime));
        }

        if (openTelemetryOptions == null)
        {
            throw new ArgumentNullException(nameof(openTelemetryOptions));
        }

        if (databaseActivity == null)
        {
            throw new ArgumentNullException(nameof(databaseActivity));
        }

        Logger = innerConnectionLifetime.Logger;
        CommandTimeout = innerConnectionLifetime.CommandTimeout;
        _innerConnectionLifetime = innerConnectionLifetime;
        _openTelemetryOptions = openTelemetryOptions;
        _databaseActivity = databaseActivity;
    }

    public ValueTask DisposeAsync()
    {
        return _innerConnectionLifetime.DisposeAsync();
    }

    public void Dispose()
    {
        _innerConnectionLifetime.Dispose();
    }

    public IMartenSessionLogger Logger { get; set; }
    public int CommandTimeout { get; }
    public int Execute(NpgsqlCommand cmd)
    {
        return _innerConnectionLifetime.Execute(cmd);
    }

    public Task<int> ExecuteAsync(NpgsqlCommand command, CancellationToken token = new CancellationToken())
    {
        return _innerConnectionLifetime.ExecuteAsync(command, token);
    }

    public DbDataReader ExecuteReader(NpgsqlCommand command)
    {
        return _innerConnectionLifetime.ExecuteReader(command);
    }

    public Task<DbDataReader> ExecuteReaderAsync(NpgsqlCommand command, CancellationToken token = default)
    {
        return _innerConnectionLifetime.ExecuteReaderAsync(command, token);
    }

    public DbDataReader ExecuteReader(NpgsqlBatch batch)
    {
        return _innerConnectionLifetime.ExecuteReader(batch);
    }

    public Task<DbDataReader> ExecuteReaderAsync(NpgsqlBatch batch, CancellationToken token = default)
    {
        return _innerConnectionLifetime.ExecuteReaderAsync(batch, token);
    }

    public void ExecuteBatchPages(IReadOnlyList<OperationPage> pages, List<Exception> exceptions)
    {
        _innerConnectionLifetime.ExecuteBatchPages(pages, exceptions);
    }

    public Task ExecuteBatchPagesAsync(IReadOnlyList<OperationPage> pages, List<Exception> exceptions, CancellationToken token)
    {
        return _innerConnectionLifetime.ExecuteBatchPagesAsync(pages, exceptions, token);
    }
}
