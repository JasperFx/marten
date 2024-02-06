using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Marten.Events.Projections;
using Microsoft.Extensions.Logging;

namespace Marten.Events.Daemon.New;

public class SubscriptionAgent: ISubscriptionAgent, IAsyncDisposable
{
    private readonly AsyncOptions _options;
    private readonly IEventLoader _loader;
    private readonly ISubscriptionExecution _execution;
    private readonly ShardStateTracker _tracker;
    private readonly ILogger _logger;
    public ShardName Name { get; }
    private readonly CancellationTokenSource _cancellation = new();
    private readonly ActionBlock<Command> _commandBlock;
    private ErrorHandlingOptions _errorOptions = new();
    private IDaemonRuntime _runtime = new NulloDaemonRuntime();

    public SubscriptionAgent(ShardName name, AsyncOptions options, IEventLoader loader,
        ISubscriptionExecution execution, ShardStateTracker tracker, ILogger logger)
    {
        _options = options;
        _loader = loader;
        _execution = execution;
        _tracker = tracker;
        _logger = logger;
        Name = name;

        _commandBlock = new ActionBlock<Command>(Apply, _cancellation.Token.SequentialOptions());

        ProjectionShardIdentity = name.Identity;
        if (_execution.DatabaseName != "Marten")
        {
            ProjectionShardIdentity += $"@{_execution.DatabaseName}";
        }
    }

    public string ProjectionShardIdentity { get; private set; }

    public CancellationToken CancellationToken => _cancellation.Token;

    // Making the setter internal so the test harness can override it
    // It's naughty, will make some people get very upset, and
    // makes unit testing much simpler. I'm not ashamed
    public long LastEnqueued { get; internal set; }

    public long LastCommitted { get; internal set; }

    public long HighWaterMark { get; internal set; }

    public async Task ReportCriticalFailureAsync(Exception ex)
    {
        // HARD STOP, and tell the daemon that you shut down.
        throw new NotImplementedException();
    }

    long ISubscriptionAgent.Position => LastCommitted;

    // TODO -- this will change when the "Pause" is put into place
    public AgentStatus Status { get; } = AgentStatus.Running;

    public async Task StopAndDrainAsync(CancellationToken token)
    {
        // Let the command block finish first
        _commandBlock.Complete();
        await _commandBlock.Completion.ConfigureAwait(false);

#if NET8_0_OR_GREATER
        await _cancellation.CancelAsync().ConfigureAwait(false);
#else
        _cancellation.Cancel();
#endif

        await _execution.StopAndDrainAsync(token).ConfigureAwait(false);
    }

    public async Task HardStopAsync()
    {
        await _execution.HardStopAsync().ConfigureAwait(false);
        await DisposeAsync().ConfigureAwait(false);
        _tracker.Publish(new ShardState(Name, LastCommitted){Action = ShardAction.Stopped});
    }

    public async Task StartAsync(SubscriptionExecutionRequest request)
    {
        Mode = request.Mode;
        _execution.Mode = request.Mode;
        _errorOptions = request.ErrorHandling;
        _runtime = request.Runtime;
        await _execution.EnsureStorageExists().ConfigureAwait(false);
        _commandBlock.Post(Command.Started(_tracker.HighWaterMark, request.Floor));
        _tracker.Publish(new ShardState(Name, request.Floor){Action = ShardAction.Started});
    }

    public void Enqueue(DeadLetterEvent @event)
    {
        _runtime.Enqueue(@event);
    }

    public async ValueTask DisposeAsync()
    {
#if NET8_0_OR_GREATER
        await _cancellation.CancelAsync().ConfigureAwait(false);
#else
        _cancellation.Cancel();
#endif
        _commandBlock.Complete();
        await _execution.DisposeAsync().ConfigureAwait(false);
    }


    internal async Task Apply(Command command)
    {
        if (_cancellation.IsCancellationRequested) return;

        switch (command.Type)
        {
            case CommandType.HighWater:
                // Ignore the high water mark if it's lower than
                // already encountered. Not sure how that could happen,
                // but still be ready for that.
                if (command.HighWaterMark <= HighWaterMark)
                {
                    return;
                }

                HighWaterMark = command.HighWaterMark;
                break;

            case CommandType.Start:
                if (command.LastCommitted > command.HighWaterMark)
                {
                    throw new InvalidOperationException(
                        $"The last committed number ({command.LastCommitted}) cannot be higher than the high water mark ({command.HighWaterMark})");
                }

                HighWaterMark = command.HighWaterMark;
                LastCommitted = LastEnqueued = command.LastCommitted;
                break;

            case CommandType.RangeCompleted:
                LastCommitted = command.LastCommitted;
                _tracker.Publish(new ShardState(Name, LastCommitted));
                break;
        }

        var inflight = LastEnqueued - LastCommitted;

        // Back pressure, slow down
        if (inflight >= _options.MaximumHopperSize) return;

        // If all caught up, do nothing!
        // Not sure how either of these numbers could actually be higher than
        // the high water mark
        if (LastCommitted >= HighWaterMark) return;
        if (LastEnqueued >= HighWaterMark) return;

        // You could maybe get a full size batch, so go get the next
        if (HighWaterMark - LastEnqueued > _options.BatchSize)
        {
            await loadNextAsync().ConfigureAwait(false);
        }
        else
        {
            // If the execution is busy, let's let events accumulate a little
            // more
            var twoBatchSize = 2 * _options.BatchSize;
            if (inflight < twoBatchSize)
            {
                await loadNextAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task loadNextAsync()
    {
        var request = new EventRequest
        {
            HighWater = HighWaterMark,
            BatchSize = _options.BatchSize,
            Floor = LastEnqueued,
            ErrorOptions = _errorOptions,
            Runtime = _runtime,
            Name = Name
        };

        // TODO -- try/catch, and you pause here if this happens.
        var page = await _loader.LoadAsync(request, _cancellation.Token).ConfigureAwait(false);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Loaded {Number} of Events from {Floor} to {Ceiling} for Subscription {Name}", page.Count, page.Floor, page.Ceiling, ProjectionShardIdentity);
        }

        LastEnqueued = page.Ceiling;

        _execution.Enqueue(page, this);
    }


    public void MarkSuccess(long processedCeiling)
    {
        _commandBlock.Post(Command.Completed(processedCeiling));
        _tracker.Publish(new ShardState(Name, processedCeiling){Action = ShardAction.Updated});
    }

    public void MarkHighWater(long sequence)
    {
        _commandBlock.Post(Command.HighWaterMarkUpdated(sequence));
    }

    public ShardExecutionMode Mode { get; private set; } = ShardExecutionMode.Continuous;



}
