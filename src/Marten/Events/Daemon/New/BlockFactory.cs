using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Marten.Events.Projections;
using Marten.Storage;

namespace Marten.Events.Daemon.New;

internal static class BlockFactory
{
    // TODO -- this is hot garbage, replace when you can
    
    public static ExecutionDataflowBlockOptions SequentialOptions(this CancellationToken token)
    {
        return new ExecutionDataflowBlockOptions
        {
            EnsureOrdered = true, MaxDegreeOfParallelism = 1, CancellationToken = token
        };
    }

    public static TransformBlock<EventRange, EventRangeGroup> BuildGrouping(IProjectionSource source,
        DocumentStore store, IMartenDatabase database, CancellationToken token)
    {
        var options = token.SequentialOptions();
        var pipeline = store.Options.ResiliencePipeline;

        // TODO -- build in communication to the parent in the case of failures getting out of the resilience
        // block
        Task<EventRangeGroup> Transform(EventRange range)
        {
            var execution = new GroupExecution(source, range, database, store);
            return pipeline.ExecuteAsync(static (x, t) => x.GroupAsync(t), execution, token).AsTask();
        }

        return new TransformBlock<EventRange, EventRangeGroup>((Func<EventRange, Task<EventRangeGroup>>)Transform,
            options);
    }
}
