using System;
using Marten.Internal.CodeGeneration;
using Marten.Schema;

namespace Marten.Storage.Metadata;

internal class DeletedAtColumn: MetadataColumn<DateTimeOffset?>, ISelectableColumn
{
    public DeletedAtColumn(): base(SchemaConstants.DeletedAtColumn, x => x.DeletedAt)
    {
        AllowNulls = true;
    }

    public bool ShouldSelect(DocumentMapping mapping, StorageStyle storageStyle)
    {
        return Member != null;
    }
}
