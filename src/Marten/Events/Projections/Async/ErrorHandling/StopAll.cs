using System;
using System.Threading.Tasks;

namespace Marten.Events.Projections.Async.ErrorHandling
{
    public class StopAll: IExceptionAction
    {
        private readonly Action<Exception> _logger;

        public StopAll(Action<Exception> logger)
        {
            _logger = logger;
        }

        public Task<ExceptionAction> Handle(Exception ex, int attempts, IMonitoredActivity activity)
        {
            _logger(ex);

            return Task.FromResult(ExceptionAction.StopAll);
        }
    }
}
