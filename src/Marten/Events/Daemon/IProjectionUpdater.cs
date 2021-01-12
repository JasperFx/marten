using System.Threading.Tasks;

namespace Marten.Events.Daemon
{
    public interface IProjectionUpdater
    {
        ProjectionUpdateBatch StartNewBatch(EventRange range);
        Task ExecuteBatch(ProjectionUpdateBatch batch);

        void StartRange(EventRange range);
    }
}
