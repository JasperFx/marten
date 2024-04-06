#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using OpenTelemetry.Trace;

namespace Marten.Internal.Sessions;

internal class EventTracingConnectionLifetime:
    IConnectionLifetime
{
    private const string MartenCommandExecutionStarted = "marten.command.execution.started";
    private const string MartenCommandExecutionFailed = "marten.command.execution.failed";
    private const string MartenBatchExecutionStarted = "marten.batch.execution.started";
    private const string MartenBatchExecutionFailed = "marten.batch.execution.failed";
    private const string MartenBatchPagesExecutionStarted = "marten.batch.pages.execution.started";
    private const string MartenBatchPagesExecutionFailed = "marten.batch.pages.execution.failed";
    private const string MartenCommandFailedExceptionType = "marten.command.failed.exception.type";
    private const string MartenCommandFailedExceptionTypes = "marten.command.failed.exception.types";
    private readonly IConnectionLifetime _innerConnectionLifetime;
    private readonly Activity? _databaseActivity;

    public EventTracingConnectionLifetime(IConnectionLifetime innerConnectionLifetime, Activity? databaseActivity = null)
    {
        if (innerConnectionLifetime == null)
        {
            throw new ArgumentNullException(nameof(innerConnectionLifetime));
        }

        Logger = innerConnectionLifetime.Logger;
        CommandTimeout = innerConnectionLifetime.CommandTimeout;
        _innerConnectionLifetime = innerConnectionLifetime;
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
        _databaseActivity?.AddEvent(new ActivityEvent(MartenCommandExecutionStarted));

        try
        {
            return _innerConnectionLifetime.Execute(cmd);
        }
        catch (Exception e)
        {
            _databaseActivity?.RecordException(e);

            throw;
        }
    }

    public Task<int> ExecuteAsync(NpgsqlCommand command, CancellationToken token = new CancellationToken())
    {
        _databaseActivity?.AddEvent(new ActivityEvent(MartenCommandExecutionStarted));

        try
        {
            return _innerConnectionLifetime.ExecuteAsync(command, token);
        }
        catch (Exception e)
        {
            _databaseActivity?.RecordException(e);
            
            throw;
        }
    }

    public DbDataReader ExecuteReader(NpgsqlCommand command)
    {
        _databaseActivity?.AddEvent(new ActivityEvent(MartenCommandExecutionStarted));

        try
        {
            return _innerConnectionLifetime.ExecuteReader(command);
        }
        catch (Exception e)
        {
            _databaseActivity?.RecordException(e);

            throw;
        }
    }

    public Task<DbDataReader> ExecuteReaderAsync(NpgsqlCommand command, CancellationToken token = default)
    {
        _databaseActivity?.AddEvent(new ActivityEvent(MartenCommandExecutionStarted));

        try
        {
            return _innerConnectionLifetime.ExecuteReaderAsync(command, token);
        }
        catch (Exception e)
        {
            _databaseActivity?.RecordException(e);

            throw;
        }
    }

    public DbDataReader ExecuteReader(NpgsqlBatch batch)
    {
        _databaseActivity?.AddEvent(new ActivityEvent(MartenBatchExecutionStarted));

        try
        {
            return _innerConnectionLifetime.ExecuteReader(batch);
        }
        catch (Exception e)
        {
            _databaseActivity?.RecordException(e);
            
            throw;
        }
    }

    public Task<DbDataReader> ExecuteReaderAsync(NpgsqlBatch batch, CancellationToken token = default)
    {
        _databaseActivity?.AddEvent(new ActivityEvent(MartenBatchExecutionStarted));

        try
        {
            return _innerConnectionLifetime.ExecuteReaderAsync(batch, token);
        }
        catch (Exception e)
        {
            _databaseActivity?.RecordException(e);

            throw;
        }
    }

    public void ExecuteBatchPages(IReadOnlyList<OperationPage> pages, List<Exception> exceptions)
    {
        _databaseActivity?.AddEvent(new ActivityEvent(MartenBatchPagesExecutionStarted));

        try
        {
            _innerConnectionLifetime.ExecuteBatchPages(pages, exceptions);
        }
        catch (AggregateException e)
        {
            _databaseActivity?.RecordException(e);

            throw;
        }
        catch (Exception e)
        {
            _databaseActivity?.RecordException(e);

            throw;
        }
    }

    public Task ExecuteBatchPagesAsync(IReadOnlyList<OperationPage> pages, List<Exception> exceptions, CancellationToken token)
    {
        _databaseActivity?.AddEvent(new ActivityEvent(MartenBatchPagesExecutionStarted));

        try
        {
            return _innerConnectionLifetime.ExecuteBatchPagesAsync(pages, exceptions, token);
        }
        catch (AggregateException e)
        {
            _databaseActivity?.RecordException(e);
            
            throw;
        }
        catch (Exception e)
        {
            _databaseActivity?.RecordException(e);


            throw;
        }
    }
}
