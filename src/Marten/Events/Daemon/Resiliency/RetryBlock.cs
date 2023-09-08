using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using JasperFx.Core;
using Microsoft.Extensions.Logging;

namespace Marten.Events.Daemon.Resiliency;

/* NOTE

All of this is copied from Wolverine. In a future release, consider
centralizing this in maybe a new shared JasperFx library


 */

internal interface IItemHandler<T>
{
    Task ExecuteAsync(T message, CancellationToken cancellation);
}

internal class LambdaItemHandler<T> : IItemHandler<T>
{
    private readonly Func<T, CancellationToken, Task> _handler;

    public LambdaItemHandler(Func<T, CancellationToken, Task> handler)
    {
        _handler = handler;
    }

    public Task ExecuteAsync(T message, CancellationToken cancellation)
    {
        return _handler(message, cancellation);
    }
}

internal class RetryBlock<T> : IDisposable
{
    private readonly ActionBlock<Item> _block;
    private readonly CancellationToken _cancellationToken;
    private readonly IItemHandler<T> _handler;
    private readonly ILogger _logger;

    public RetryBlock(Func<T, CancellationToken, Task> handler, ILogger logger, CancellationToken cancellationToken,
        Action<ExecutionDataflowBlockOptions>? configure = null)
        : this(new LambdaItemHandler<T>(handler), logger, cancellationToken, configure)
    {
    }

    public RetryBlock(Func<T, CancellationToken, Task> handler, ILogger logger, CancellationToken cancellationToken,
        ExecutionDataflowBlockOptions options)
    {
        _handler = new LambdaItemHandler<T>(handler);
        _logger = logger;

        options.CancellationToken = cancellationToken;
        options.SingleProducerConstrained = true;

        _cancellationToken = cancellationToken;

        _block = new ActionBlock<Item>(executeAsync, options);
    }

    public RetryBlock(IItemHandler<T> handler, ILogger logger, CancellationToken cancellationToken,
        Action<ExecutionDataflowBlockOptions>? configure = null)
    {
        _handler = handler;
        _logger = logger;
        var options = new ExecutionDataflowBlockOptions
        {
            CancellationToken = cancellationToken,
            SingleProducerConstrained = true
        };

        configure?.Invoke(options);

        _cancellationToken = cancellationToken;

        _block = new ActionBlock<Item>(executeAsync, options);
    }

    public int MaximumAttempts { get; set; } = 3;
    public TimeSpan[] Pauses { get; set; } = { 50.Milliseconds(), 100.Milliseconds(), 250.Milliseconds() };

    public void Dispose()
    {
        _block.Complete();
    }

    public void Post(T message)
    {
        var item = new Item(message);
        _block.Post(item);
    }

    public async Task PostAsync(T message)
    {
        try
        {
            await _handler.ExecuteAsync(message, _cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error while trying to retry {Item}", message);
            Post(message);
        }
    }

    public TimeSpan DeterminePauseTime(int attempt)
    {
        if (attempt >= Pauses.Length)
        {
            return Pauses.LastOrDefault();
        }

        return Pauses[attempt - 1];
    }

    private async Task executeAsync(Item item)
    {
        try
        {
            item.Attempts++;

            var pause = DeterminePauseTime(item.Attempts);
            await Task.Delay(pause, _cancellationToken).ConfigureAwait(false);

            await _handler.ExecuteAsync(item.Message, _cancellationToken).ConfigureAwait(false);

            _logger.LogDebug("Completed {Item}", item.Message);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error while trying to retry {Item}", item.Message);

            if (item.Attempts < MaximumAttempts)
            {
                _block.Post(item);
            }
            else
            {
                _logger.LogInformation("Discarding message {Message} after {Attempts} attempts", item.Message,
                    item.Attempts);
            }
        }
    }

    public Task DrainAsync()
    {
        _block.Complete();
        return _block.Completion;
    }

    public class Item
    {
        public Item(T item)
        {
            Message = item;
            Attempts = 0;
        }

        public int Attempts { get; set; }
        public T Message { get; }
    }
}
