using System;
using System.Threading.Tasks;

namespace Marten.Events.Projections.Async.ErrorHandling
{
    public class Pause: IExceptionAction
    {
        public Task<ExceptionAction> Handle(Exception ex, int attempts, IMonitoredActivity activity)
        {
            return Task.FromResult(ExceptionAction.Pause);
        }
    }
}
