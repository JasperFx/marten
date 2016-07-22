using System;
using System.Threading.Tasks;

namespace Marten.Events.Projections.Async.ErrorHandling
{
    public interface IExceptionAction
    {
        Task<ExceptionAction> Handle(Exception ex, int attempts, IMonitoredActivity activity);
    }
}