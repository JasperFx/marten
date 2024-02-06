using System.Threading;
using System.Threading.Tasks.Dataflow;

namespace Marten.Events.Daemon.New;

internal static class BlockExtensions
{
    public static ExecutionDataflowBlockOptions SequentialOptions(this CancellationToken token)
    {
        return new ExecutionDataflowBlockOptions
        {
            EnsureOrdered = true, MaxDegreeOfParallelism = 1, CancellationToken = token
        };
    }
}
