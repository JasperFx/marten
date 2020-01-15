using System.Threading.Tasks;

namespace Marten.Events.Projections.Async.ErrorHandling
{
    public interface IMonitoredActivity
    {
        Task Stop();

        Task Start();
    }
}
