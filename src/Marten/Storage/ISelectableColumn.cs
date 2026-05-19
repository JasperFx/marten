using Marten.Internal.CodeGeneration;
using Marten.Schema;

namespace Marten.Storage;

internal interface ISelectableColumn
{
    string Name { get; }

    bool ShouldSelect(DocumentMapping mapping, StorageStyle storageStyle);
}
