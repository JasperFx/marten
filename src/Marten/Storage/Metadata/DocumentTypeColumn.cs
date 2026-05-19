using Marten.Internal.CodeGeneration;
using Marten.Schema;

namespace Marten.Storage.Metadata;

internal class DocumentTypeColumn: MetadataColumn<string>, ISelectableColumn
{
    public DocumentTypeColumn(DocumentMapping mapping): base(SchemaConstants.DocumentTypeColumn, x => x.DocumentType)
    {
        DefaultExpression = $"'{mapping.AliasFor(mapping.DocumentType)}'";
    }

    public bool ShouldSelect(DocumentMapping mapping, StorageStyle storageStyle)
    {
        return true;
    }
}
