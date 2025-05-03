using Marten.Internal.CodeGeneration;
using Marten.Schema;

namespace Marten.Internal.Storage;

public abstract class DirtyCheckedDocumentStorage<T, TId>: IdentityMapDocumentStorage<T, TId> where TId : notnull where T : notnull
{
    public DirtyCheckedDocumentStorage(DocumentMapping document): base(StorageStyle.DirtyTracking, document)
    {
    }
}
