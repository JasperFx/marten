using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Marten.Events.Daemon.New;

public class SubscriptionAgent: ISubscriptionAgent, IAsyncDisposable
{
    private readonly AsyncOptions _options;
    private readonly IEventLoader _loader;
    private readonly ISubscriptionExecution _execution;
    public string Identifier { get; }
    private readonly CancellationTokenSource _cancellation = new();
    private readonly ActionBlock<Command> _commandBlock;

    public SubscriptionAgent(string identifier, AsyncOptions options, IEventLoader loader, ISubscriptionExecution execution)
    {
        _options = options;
        _loader = loader;
        _execution = execution;
        Identifier = identifier;

        _commandBlock = new ActionBlock<Command>(Apply, _cancellation.Token.SequentialOptions());
    }

    public CancellationToken CancellationToken => _cancellation.Token;

    // Making the setter internal so the test harness can override it
    // It's naughty, will make some people get very upset, and
    // makes unit testing much simpler. I'm not ashamed
    public long LastEnqueued { get; internal set; }

    public long LastCommitted { get; internal set; }

    public long HighWaterMark { get; internal set; }

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

    internal void PostCommand(Command command) => _commandBlock.Post(command);

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
                LastCommitted = command.Range.SequenceCeiling;
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
            HighWater = HighWaterMark, BatchSize = _options.BatchSize, Floor = LastEnqueued
        };

        // TODO -- try/catch, and you pause here if this happens.
        var page = await _loader.LoadAsync(request, _cancellation.Token).ConfigureAwait(false);

        LastEnqueued = page.Ceiling;

        _execution.Enqueue(page, this);
    }

    public void Pause(TimeSpan time)
    {
        throw new NotImplementedException();
    }
}
