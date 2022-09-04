using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Baseline.Dates;

namespace Marten.CommandLine.Commands.Projection;

internal class BatchingBlock<T> : IDisposable
{
    private readonly BatchBlock<T> _batchBlock;
    private readonly TimeSpan _timeSpan;
    private readonly Timer _trigger;

    public BatchingBlock(int milliseconds, ITargetBlock<T[]> processor,
        CancellationToken cancellation = default)
        : this(milliseconds.Milliseconds(), processor, cancellation)
    {
    }

    public BatchingBlock(TimeSpan timeSpan, ITargetBlock<T[]> processor,
        CancellationToken cancellation = default)
    {
        _timeSpan = timeSpan;
        _batchBlock = new BatchBlock<T>(100, new GroupingDataflowBlockOptions
        {
            CancellationToken = cancellation,
            BoundedCapacity = DataflowBlockOptions.Unbounded
        });

        _trigger = new Timer(_ =>
        {
            try
            {
                _batchBlock.TriggerBatch();
            }
            catch (Exception)
            {
                // ignored
            }
        }, null, Timeout.Infinite, Timeout.Infinite);


        _batchBlock.LinkTo(processor);
    }

    public int ItemCount => _batchBlock.OutputCount;

    public Task Completion => _batchBlock.Completion;


    public void Dispose()
    {
        _trigger.Dispose();
        _batchBlock.Complete();
    }

    public Task SendAsync(T item)
    {
        try
        {
            _trigger.Change(_timeSpan, Timeout.InfiniteTimeSpan);
        }
        catch (Exception)
        {
            // ignored
        }

        return _batchBlock.SendAsync(item);
    }

    public void Complete()
    {
        _batchBlock.Complete();
    }
}