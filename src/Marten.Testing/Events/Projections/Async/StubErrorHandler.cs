using System;
using System.Threading.Tasks;
using Marten.Events.Projections.Async.ErrorHandling;

namespace Marten.Testing.Events.Projections.Async
{
    public class StubErrorHandler: IDaemonErrorHandler
    {
        public Task TryAction(Func<Task> action, IMonitoredActivity activity, int attempts = 0)
        {
            return action();
        }
    }
}
