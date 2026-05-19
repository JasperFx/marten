using System;
using Marten.Internal.CodeGeneration;
using Marten.Internal.Sessions;
using Marten.Schema;
using Weasel.Postgresql;

namespace Marten.Storage.Metadata;

internal class LastModifiedColumn: MetadataColumn<DateTimeOffset>, ISelectableColumn
{
    public LastModifiedColumn(): base(SchemaConstants.LastModifiedColumn, x => x.LastModified)
    {
        DefaultExpression = "(transaction_timestamp())";
        Type = "timestamp with time zone";

        ShouldUpdatePartials = true;
    }

    public bool ShouldSelect(DocumentMapping mapping, StorageStyle storageStyle)
    {
        return Member != null;
    }

    public override void WriteMetadataInUpdateStatement(ICommandBuilder builder, DocumentSessionBase session)
    {
        builder.Append(SchemaConstants.LastModifiedColumn);
        builder.Append(" = (now() at time zone 'utc')");
    }
}
