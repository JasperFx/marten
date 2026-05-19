using Marten.Internal.CodeGeneration;
using Marten.Internal.Sessions;
using Marten.Schema;
using Marten.Schema.Arguments;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Storage.Metadata;

internal class LastModifiedByColumn: MetadataColumn<string>, ISelectableColumn
{
    public static readonly string ColumnName = "last_modified_by";

    public LastModifiedByColumn(): base(ColumnName, x => x.LastModifiedBy)
    {
        Enabled = false;
        ShouldUpdatePartials = true;
    }

    public bool ShouldSelect(DocumentMapping mapping, StorageStyle storageStyle)
    {
        return mapping.Metadata.LastModifiedBy.EnabledWithMember();
    }

    internal override UpsertArgument ToArgument()
    {
        return new LastModifiedByArgument();
    }

    public override void WriteMetadataInUpdateStatement(ICommandBuilder builder, DocumentSessionBase session)
    {
        builder.Append(ColumnName);
        builder.Append(" = ");
        builder.AppendParameter(session.LastModifiedBy);
    }
}

internal class LastModifiedByArgument: UpsertArgument
{
    public LastModifiedByArgument()
    {
        Arg = "lastModifiedBy";
        Column = LastModifiedByColumn.ColumnName;
        PostgresType = "varchar";
        DbType = NpgsqlDbType.Varchar;
    }
}
