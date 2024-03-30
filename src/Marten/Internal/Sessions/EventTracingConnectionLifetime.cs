using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;
using Npgsql;

namespace Marten.Internal.Sessions;

internal class EventTracingConnectionLifetime :
    IConnectionLifetime
{
    private const string MartenCommandExecutionStarted = "MartenCommandExecutionStarted";
    private const string MartenCommandExecutionFailed = "MartenCommandExecutionFailed";
    private const string MartenBatchExecutionStarted = "MartenBatchExecutionStarted";
    private const string MartenBatchExecutionFailed = "MartenBatchExecutionFailed";
    private const string MartenBatchPagesExecutionStarted = "MartenBatchPagesExecutionStarted";
    private const string MartenBatchPagesExecutionFailed = "MartenBatchPagesExecutionFailed";

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
        if (_openTelemetryOptions.TrackConnectionEvents)
        {
            _databaseActivity.AddEvent(new ActivityEvent(MartenCommandExecutionStarted));
        }

        try
        {
            return _innerConnectionLifetime.Execute(cmd);
        }
        catch (Exception e)
        {
            if (_openTelemetryOptions.TrackConnectionEvents)
            {
                _databaseActivity.AddEvent(new ActivityEvent(MartenCommandExecutionFailed, DateTimeOffset.UtcNow, new ActivityTagsCollection(new KeyValuePair<string, object>[] {new KeyValuePair<string, object>("ExceptionType", e.GetType()) })));
            }

            throw;
        }
    }

    public Task<int> ExecuteAsync(NpgsqlCommand command, CancellationToken token = new CancellationToken())
    {
        if (_openTelemetryOptions.TrackConnectionEvents)
        {
            _databaseActivity.AddEvent(new ActivityEvent("Database command execution started"));
        }

        try
        {
            return _innerConnectionLifetime.ExecuteAsync(command, token);
        }
        catch (Exception e)
        {
            if (_openTelemetryOptions.TrackConnectionEvents)
            {
                _databaseActivity.AddEvent(new ActivityEvent(MartenCommandExecutionFailed, DateTimeOffset.UtcNow,
                    new ActivityTagsCollection(new[]
                    {
                        new KeyValuePair<string, object>("ExceptionType", e.GetType())
                    })));
            }

            throw;
        }
    }

    public DbDataReader ExecuteReader(NpgsqlCommand command)
    {
        if (_openTelemetryOptions.TrackConnectionEvents)
        {
            _databaseActivity.AddEvent(new ActivityEvent("Database command execution started"));
        }

        try
        {
            return _innerConnectionLifetime.ExecuteReader(command);
        }
        catch (Exception e)
        {
            if (_openTelemetryOptions.TrackConnectionEvents)
            {
                _databaseActivity.AddEvent(new ActivityEvent(MartenCommandExecutionFailed, DateTimeOffset.UtcNow,
                    new ActivityTagsCollection(new[]
                    {
                        new KeyValuePair<string, object>("ExceptionType", e.GetType())
                    })));
            }

            throw;
        }
    }

    public Task<DbDataReader> ExecuteReaderAsync(NpgsqlCommand command, CancellationToken token = default)
    {
        if (_openTelemetryOptions.TrackConnectionEvents)
        {
            _databaseActivity.AddEvent(new ActivityEvent("Database command execution started"));
        }

        try
        {
return _innerConnectionLifetime.ExecuteReaderAsync(command, token);
        }
        catch (Exception e)
        {
            if (_openTelemetryOptions.TrackConnectionEvents)
            {
                _databaseActivity.AddEvent(new ActivityEvent(MartenCommandExecutionFailed, DateTimeOffset.UtcNow,
                    new ActivityTagsCollection(new[]
                    {
                        new KeyValuePair<string, object>("ExceptionType", e.GetType())
                    })));
            }

            throw;
        }
    }

    public DbDataReader ExecuteReader(NpgsqlBatch batch)
    {
        if (_openTelemetryOptions.TrackConnectionEvents)
        {
            _databaseActivity.AddEvent(new ActivityEvent(MartenBatchExecutionStarted));
        }

        try
        {
            return _innerConnectionLifetime.ExecuteReader(batch);
        }
        catch (Exception e)
        {
            if (_openTelemetryOptions.TrackConnectionEvents)
            {
                _databaseActivity.AddEvent(new ActivityEvent(MartenBatchExecutionFailed, DateTimeOffset.UtcNow, new ActivityTagsCollection(new [] { new KeyValuePair<string, object>("ExceptionType", e.GetType())})));
            }

            throw;
        }
    }

    public Task<DbDataReader> ExecuteReaderAsync(NpgsqlBatch batch, CancellationToken token = default)
    {
        if (_openTelemetryOptions.TrackConnectionEvents)
        {
            _databaseActivity.AddEvent(new ActivityEvent(MartenBatchExecutionStarted));
        }

        try
        {
            return _innerConnectionLifetime.ExecuteReaderAsync(batch, token);
        }
        catch (Exception e)
        {
            if (_openTelemetryOptions.TrackConnectionEvents)
            {
                _databaseActivity.AddEvent(new ActivityEvent(MartenBatchExecutionFailed, DateTimeOffset.UtcNow, new ActivityTagsCollection(new[] { new KeyValuePair<string, object>("ExceptionType", e.GetType()) })));
            }

            throw;
        }
    }

    public void ExecuteBatchPages(IReadOnlyList<OperationPage> pages, List<Exception> exceptions)
    {
        if (_openTelemetryOptions.TrackConnectionEvents)
        {
            _databaseActivity.AddEvent(new ActivityEvent(MartenBatchPagesExecutionStarted));
        }

        try
        {
            _innerConnectionLifetime.ExecuteBatchPages(pages, exceptions);
        }
        catch (AggregateException e)
        {
            if (_openTelemetryOptions.TrackConnectionEvents)
            {
                var innerExceptionTypes = e.InnerExceptions.Select(t => t.GetType());
                _databaseActivity.AddEvent(new ActivityEvent(MartenBatchPagesExecutionFailed, DateTimeOffset.UtcNow,
                    new ActivityTagsCollection(new[]
                    {
                        new KeyValuePair<string, object>("ExceptionTypes", innerExceptionTypes)
                    })));
            }

            throw;
        }
        catch (Exception e)
        {
            if (_openTelemetryOptions.TrackConnectionEvents)
            {
                _databaseActivity.AddEvent(new ActivityEvent(MartenBatchPagesExecutionFailed, DateTimeOffset.UtcNow,
                    new ActivityTagsCollection(new[]
                    {
                        new KeyValuePair<string, object>("ExceptionType", e.GetType())
                    })));
            }

            throw;
        }
    }

    public Task ExecuteBatchPagesAsync(IReadOnlyList<OperationPage> pages, List<Exception> exceptions, CancellationToken token)
    {
        if (_openTelemetryOptions.TrackConnectionEvents)
        {
            _databaseActivity.AddEvent(new ActivityEvent(MartenBatchPagesExecutionStarted));
        }

        try
        {
            return _innerConnectionLifetime.ExecuteBatchPagesAsync(pages, exceptions, token);
        }
        catch (AggregateException e)
        {
            if (_openTelemetryOptions.TrackConnectionEvents)
            {
                var innerExceptionTypes = e.InnerExceptions.Select(t => t.GetType());
                _databaseActivity.AddEvent(new ActivityEvent(MartenBatchPagesExecutionFailed, DateTimeOffset.UtcNow,
                    new ActivityTagsCollection(new[]
                    {
                        new KeyValuePair<string, object>("ExceptionTypes", innerExceptionTypes)
                    })));
            }

            throw;
        }
        catch (Exception e)
        {
            if (_openTelemetryOptions.TrackConnectionEvents)
            {
                _databaseActivity.AddEvent(new ActivityEvent(MartenBatchPagesExecutionFailed, DateTimeOffset.UtcNow,
                    new ActivityTagsCollection(new[]
                    {
                        new KeyValuePair<string, object>("ExceptionType", e.GetType())
                    })));
            }

            throw;
        }
    }
}
