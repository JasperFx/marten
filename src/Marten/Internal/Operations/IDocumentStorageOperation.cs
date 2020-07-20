using Marten.Internal.DirtyTracking;

namespace Marten.Internal.Operations
{
    public interface IDocumentStorageOperation : IStorageOperation
    {
        object Document { get; }
        IChangeTracker ToTracker(IMartenSession session);
    }
}
