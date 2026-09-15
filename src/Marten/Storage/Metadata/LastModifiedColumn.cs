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
        // ⚠️ transaction_timestamp(), NOT (now() at time zone 'utc') -- marten#5379, diagnosed in
        // marten#5136. `now()` returns timestamptz; `AT TIME ZONE 'utc'` STRIPS the offset and yields a
        // naive timestamp holding UTC wall-clock time, and assigning that back to a
        // `timestamp with time zone` column re-interprets it in the session's TimeZone. On a database
        // at UTC+2 the stored instant is two hours in the past -- a genuinely different point in time,
        // not a display artifact. Matches this column's own DEFAULT, which was always correct, so a row
        // no longer jumps by the offset the first time it is patched.
        //
        // ⚠️ Reached ONLY from PatchOperation, which is the sole caller of this method -- an ordinary
        // upsert stamps the column from its DEFAULT. And a test for it is invisible at UTC, where both
        // spellings are byte-identical. See last_modified_on_a_non_utc_connection.
        builder.Append(" = (transaction_timestamp())");
    }
}
