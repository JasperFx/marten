using System;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Daemon
{
    public interface IProjectionUpdater
    {
        ProjectionUpdateBatch StartNewBatch(EventRange range, CancellationToken token);
        Task ExecuteBatch(ProjectionUpdateBatch batch);

        void StartRange(EventRange range);

        Task TryAction(Func<Task> action, CancellationToken token);
    }
}
