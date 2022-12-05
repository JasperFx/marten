#nullable enable
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Internal;

public interface IUpdateBatch
{
    void ApplyChanges(IMartenSession session);
    Task ApplyChangesAsync(IMartenSession session, CancellationToken token);
}
