using System;
using Marten.Internal.CodeGeneration;
using Marten.Internal.Sessions;
using Marten.Schema;
using Weasel.Postgresql;

namespace Marten.Storage.Metadata;

internal class VersionColumn: MetadataColumn<Guid>, ISelectableColumn
{
    public VersionColumn(): base(SchemaConstants.VersionColumn, x => x.CurrentVersion)
    {
        AllowNulls = false;
        DefaultExpression = "(md5(random()::text || clock_timestamp()::text)::uuid)";
        ShouldUpdatePartials = true;
    }

    public bool ShouldSelect(DocumentMapping mapping, StorageStyle storageStyle)
    {
        if (Member != null)
        {
            return true;
        }

        return storageStyle != StorageStyle.QueryOnly && mapping.UseOptimisticConcurrency;
    }

    public override void WriteMetadataInUpdateStatement(ICommandBuilder builder, DocumentSessionBase session)
    {
        builder.Append(SchemaConstants.VersionColumn);
        builder.Append(" = ");
        builder.AppendParameter(Guid.NewGuid());
    }
}
