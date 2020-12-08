using System.Threading;
using System.Threading.Tasks;

namespace Marten.Internal
{
    // TODO -- need a version that builds up commands as it receives IStorageOperations
    public interface IUpdateBatch
    {
        void ApplyChanges(IMartenSession session);
        Task ApplyChangesAsync(IMartenSession session, CancellationToken token);
    }
}
