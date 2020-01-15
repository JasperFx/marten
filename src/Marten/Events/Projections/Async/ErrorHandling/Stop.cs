using System;
using System.Threading.Tasks;

namespace Marten.Events.Projections.Async.ErrorHandling
{
    public class Stop: IExceptionAction
    {
        private readonly Action<Exception> _logger;

        public Stop(Action<Exception> logger)
        {
            _logger = logger;
        }

        public Task<ExceptionAction> Handle(Exception ex, int attempts, IMonitoredActivity activity)
        {
            _logger(ex);
            return Task.FromResult(ExceptionAction.Stop);
        }
    }
}
