using JasperFx.CodeGeneration;
using Marten.Internal.CodeGeneration;
using Marten.Schema;

namespace Marten.Storage;

internal interface ISelectableColumn
{
    string Name { get; }

    void GenerateCode(StorageStyle storageStyle, GeneratedType generatedType, GeneratedMethod async,
        GeneratedMethod sync, int index,
        DocumentMapping mapping);

    bool ShouldSelect(DocumentMapping mapping, StorageStyle storageStyle);
}
