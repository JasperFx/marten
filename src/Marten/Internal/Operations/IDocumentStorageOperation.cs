#nullable enable

namespace Marten.Internal.Operations;

public interface IDocumentStorageOperation: IStorageOperation
{
    object Document { get; }
    IChangeTracker ToTracker(IStorageSession session);
}
