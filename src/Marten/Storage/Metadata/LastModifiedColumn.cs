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

        // #5379: the same expression as DefaultExpression above. This used to be
        // `now() at time zone 'utc'`, which strips the offset to a naive timestamp that Postgres
        // then re-interprets in the session TimeZone on assignment to a timestamptz column, so a
        // patch on any non-UTC database stamped an instant off by the UTC offset.
        builder.Append(" = (transaction_timestamp())");
    }
}
