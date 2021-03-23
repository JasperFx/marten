using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Marten.Events.Daemon
{
    /// <summary>
    /// Used internally by asynchronous projections.
    /// </summary>
    // This is public because it's used by the generated code
    public interface IShardAgent
    {
        ProjectionUpdateBatch StartNewBatch(EventRangeGroup group);
        Task ExecuteBatch(ProjectionUpdateBatch batch);

        void StartRange(EventRange range);

        Task TryAction(Func<Task> action, CancellationToken token, Action<ILogger, Exception> logException = null, EventRangeGroup group = null, GroupActionMode actionMode = GroupActionMode.Parent);

        bool IsStopping();
    }
}
