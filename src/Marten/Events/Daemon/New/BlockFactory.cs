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


}
