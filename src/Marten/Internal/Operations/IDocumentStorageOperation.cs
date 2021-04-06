using Marten.Internal.DirtyTracking;
#nullable enable
namespace Marten.Internal.Operations
{
    public interface IDocumentStorageOperation : IStorageOperation
    {
        object Document { get; }
        IChangeTracker ToTracker(IMartenSession session);
    }
}
