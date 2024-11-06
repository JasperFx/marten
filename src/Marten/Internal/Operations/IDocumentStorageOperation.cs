#nullable enable
using Marten.Internal.DirtyTracking;
using Weasel.Core.Operations;

namespace Marten.Internal.Operations;

public interface IDocumentStorageOperation: IStorageOperation
{
    object Document { get; }
    IChangeTracker ToTracker(IStorageSession session);
}
