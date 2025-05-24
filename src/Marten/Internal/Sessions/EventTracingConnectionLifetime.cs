#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Descriptors;
using Marten.Events.Operations;
using Marten.Internal.OpenTelemetry;
using Marten.Internal.Operations;
using Marten.Services;
using Npgsql;

namespace Marten.Internal.Sessions;

internal class EventTracingConnectionLifetime:
    IConnectionLifetime
{
    private const string MartenCommandExecutionStarted = "marten.command.execution.started";
    private const string MartenBatchExecutionStarted = "marten.batch.execution.started";
    private const string MartenBatchPagesExecutionStarted = "marten.batch.pages.execution.started";
    private readonly IConnectionLifetime _innerConnectionLifetime;
    private readonly OpenTelemetryOptions _telemetryOptions;
    private readonly Activity? _databaseActivity;

    public EventTracingConnectionLifetime(IConnectionLifetime innerConnectionLifetime, string tenantId,
        OpenTelemetryOptions telemetryOptions)
    {
        if (innerConnectionLifetime == null)
        {
            throw new ArgumentNullException(nameof(innerConnectionLifetime));
        }

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("The tenant id cannot be null, an empty string or whitespace.", nameof(tenantId));
        }

        Logger = innerConnectionLifetime.Logger;
        CommandTimeout = innerConnectionLifetime.CommandTimeout;
        _innerConnectionLifetime = innerConnectionLifetime;
        _telemetryOptions = telemetryOptions;

        var currentActivity = Activity.Current ?? null;
        var tags = new ActivityTagsCollection(new[] { new KeyValuePair<string, object?>(OtelConstants.TenantId, tenantId) });
        _databaseActivity = MartenTracing.StartConnectionActivity(currentActivity, tags);
    }

    public ValueTask DisposeAsync()
    {
        _databaseActivity?.Stop();
        return _innerConnectionLifetime.DisposeAsync();
    }

    public void Dispose()
    {
        _databaseActivity?.Stop();
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
            _databaseActivity?.AddException(e);

            throw;
        }
    }

    public async Task<int> ExecuteAsync(NpgsqlCommand command, CancellationToken token = new CancellationToken())
    {
        _databaseActivity?.AddEvent(new ActivityEvent(MartenCommandExecutionStarted));

        try
        {
            return await _innerConnectionLifetime.ExecuteAsync(command, token).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _databaseActivity?.AddException(e);

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
            _databaseActivity?.AddException(e);

            throw;
        }
    }

    public async Task<DbDataReader> ExecuteReaderAsync(NpgsqlCommand command, CancellationToken token = default)
    {
        _databaseActivity?.AddEvent(new ActivityEvent(MartenCommandExecutionStarted));

        try
        {
            return await _innerConnectionLifetime.ExecuteReaderAsync(command, token).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _databaseActivity?.AddException(e);

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
            _databaseActivity?.AddException(e);

            throw;
        }
    }

    public async Task<DbDataReader> ExecuteReaderAsync(NpgsqlBatch batch, CancellationToken token = default)
    {
        _databaseActivity?.AddEvent(new ActivityEvent(MartenBatchExecutionStarted));

        try
        {
            return await _innerConnectionLifetime.ExecuteReaderAsync(batch, token).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _databaseActivity?.AddException(e);

            throw;
        }
    }

    public void ExecuteBatchPages(IReadOnlyList<OperationPage> pages, List<Exception> exceptions)
    {
        _databaseActivity?.AddEvent(new ActivityEvent(MartenBatchPagesExecutionStarted));

        try
        {
            _innerConnectionLifetime.ExecuteBatchPages(pages, exceptions);
            writeVerboseEvents(pages);
        }
        catch (AggregateException e)
        {
            _databaseActivity?.AddException(e);

            throw;
        }
        catch (Exception e)
        {
            _databaseActivity?.AddException(e);

            throw;
        }
    }

    public async Task ExecuteBatchPagesAsync(IReadOnlyList<OperationPage> pages, List<Exception> exceptions, CancellationToken token)
    {
        _databaseActivity?.AddEvent(new ActivityEvent(MartenBatchPagesExecutionStarted));

        try
        {
            await _innerConnectionLifetime.ExecuteBatchPagesAsync(pages, exceptions, token).ConfigureAwait(false);

            writeVerboseEvents(pages);
        }
        catch (AggregateException e)
        {
            _databaseActivity?.AddException(e);
            throw;
        }
        catch (Exception e)
        {
            _databaseActivity?.AddException(e);
            throw;
        }
    }

    private void writeVerboseEvents(IReadOnlyList<OperationPage> pages)
    {
        if (_telemetryOptions.TrackConnections == TrackLevel.Verbose)
        {
            var ops = pages.SelectMany(x => x.Operations);
            foreach (var op in ops)
            {
                if (op is AppendEventOperationBase eventOp)
                {
                    _databaseActivity?.AddEvent(new ActivityEvent("marten.append.event",
                        tags: new ActivityTagsCollection { { "Type", eventOp.Event.EventTypeName } }));
                }
                else if (op.Role() != OperationRole.Events)
                {
                    _databaseActivity?.AddEvent(new ActivityEvent("marten." + op.Role().ToString().ToLower(),
                        tags: new ActivityTagsCollection { { "Type", op.DocumentType?.Name } }));
                }


            }
        }
    }
}
