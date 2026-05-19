using System;
using Marten.Internal.CodeGeneration;
using Marten.Schema;

namespace Marten.Storage.Metadata;

internal class CreatedAtColumn: MetadataColumn<DateTimeOffset>, ISelectableColumn
{
    public CreatedAtColumn(): base(SchemaConstants.CreatedAtColumn, x => x.CreatedAt)
    {
        DefaultExpression = "(transaction_timestamp())";
        Type = "timestamp with time zone";
        Enabled = false;
    }

    public bool ShouldSelect(DocumentMapping mapping, StorageStyle storageStyle)
    {
        return Member != null;
    }
}
