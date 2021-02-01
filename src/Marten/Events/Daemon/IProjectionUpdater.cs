using System;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Daemon
{
    /// <summary>
    /// Used internally by asynchronous projections.
    /// </summary>
    // This is public because it's used by the generated code
    public interface IProjectionUpdater
    {
        ProjectionUpdateBatch StartNewBatch(EventRange range, CancellationToken token);
        Task ExecuteBatch(ProjectionUpdateBatch batch);

        void StartRange(EventRange range);

        Task TryAction(Func<Task> action, CancellationToken token);
    }
}
