using System.Threading.Tasks;

namespace Marten.Events.Projections.Async
{
    public interface IDaemonUpdate
    {
        Task Invoke(ProjectionTrack projectionTrack);
    }
}