#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImTools;
using JasperFx;
using JasperFx.Descriptors;
using Marten.Events.Operations;
using Marten.Internal.OpenTelemetry;
using Marten.Internal.Operations;
using Marten.Services;
using Npgsql;

namespace Marten.Internal.Sessions;

internal class EventTracingConnectionLifetime:
    IConnectionLifetime, ITransactionStarter
{
    private const string MartenCommandExecutionStarted = "marten.command.execution.started";
    private const string MartenBatchExecutionStarted = "marten.batch.execution.started";
    private const string MartenBatchPagesExecutionStarted = "marten.batch.pages.execution.started";
    private readonly OpenTelemetryOptions _telemetryOptions;
    private readonly Activity? _databaseActivity;
    private readonly string _tenantId;

    public EventTracingConnectionLifetime(IConnectionLifetime innerConnectionLifetime, string tenantId,
        OpenTelemetryOptions telemetryOptions)
    {
        ArgumentNullException.ThrowIfNull(innerConnectionLifetime);

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("The tenant id cannot be null, an empty string or whitespace.", nameof(tenantId));
        }

        InnerConnectionLifetime = innerConnectionLifetime;
        Logger = innerConnectionLifetime.Logger;
        CommandTimeout = innerConnectionLifetime.CommandTimeout;
        _telemetryOptions = telemetryOptions;

        var currentActivity = Activity.Current ?? null;
        var tags = new ActivityTagsCollection([new KeyValuePair<string, object?>(OtelConstants.TenantId, tenantId)]);
        _databaseActivity = MartenTracing.StartConnectionActivity(currentActivity, tags);

        _tenantId = tenantId;
    }

    public EventTracingConnectionLifetime(OpenTelemetryOptions telemetryOptions, Activity? databaseActivity, IConnectionLifetime innerConnectionLifetime, IMartenSessionLogger logger)
    {
        _telemetryOptions = telemetryOptions;
        _databaseActivity = databaseActivity;
        InnerConnectionLifetime = innerConnectionLifetime;
        Logger = logger;
    }

    public IConnectionLifetime InnerConnectionLifetime { get; }

    public ValueTask DisposeAsync()
    {
        _databaseActivity?.Stop();
        return InnerConnectionLifetime.DisposeAsync();
    }

    public void Dispose()
    {
        _databaseActivity?.Stop();
        InnerConnectionLifetime.Dispose();
    }

    public IMartenSessionLogger Logger { get => InnerConnectionLifetime.Logger; set => InnerConnectionLifetime.Logger = value; }
    public int CommandTimeout { get; }
    public int Execute(NpgsqlCommand cmd)
    {
        _databaseActivity?.AddEvent(new ActivityEvent(MartenCommandExecutionStarted));

        try
        {
            return InnerConnectionLifetime.Execute(cmd);
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
            return await InnerConnectionLifetime.ExecuteAsync(command, token).ConfigureAwait(false);
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
            return InnerConnectionLifetime.ExecuteReader(command);
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
            return await InnerConnectionLifetime.ExecuteReaderAsync(command, token).ConfigureAwait(false);
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
            return InnerConnectionLifetime.ExecuteReader(batch);
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
            return await InnerConnectionLifetime.ExecuteReaderAsync(batch, token).ConfigureAwait(false);
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
            InnerConnectionLifetime.ExecuteBatchPages(pages, exceptions);
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
            await InnerConnectionLifetime.ExecuteBatchPagesAsync(pages, exceptions, token).ConfigureAwait(false);

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

    public IAlwaysConnectedLifetime Start()
    {
        if (InnerConnectionLifetime is ITransactionStarter starter) return starter.Start();

        throw new InvalidOperationException(
            $"The inner connection lifetime {InnerConnectionLifetime} does not implement {nameof(ITransactionStarter)}");
    }

    public Task<IAlwaysConnectedLifetime> StartAsync(CancellationToken token)
    {
        if (InnerConnectionLifetime is ITransactionStarter starter) return starter.StartAsync(token);

        throw new InvalidOperationException(
            $"The inner connection lifetime {InnerConnectionLifetime} does not implement {nameof(ITransactionStarter)}");
    }
}
