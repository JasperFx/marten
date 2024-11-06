using Marten.Internal.Operations;
using Weasel.Core.Operations;

namespace Marten.Internal.DirtyTracking;

public interface IChangeTracker
{
    object Document { get; }
    bool DetectChanges(IMartenSession session, out IStorageOperation operation);
    void Reset(IMartenSession session);
}
