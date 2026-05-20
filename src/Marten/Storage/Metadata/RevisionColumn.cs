using System;
using JasperFx.Core;
using Marten.Internal.CodeGeneration;
using Marten.Internal.Sessions;
using Marten.Schema;
using Marten.Schema.Arguments;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.Storage.Metadata;

internal class RevisionColumn: MetadataColumn<long>, ISelectableColumn
{
    public RevisionColumn(): base(SchemaConstants.VersionColumn, x => x.CurrentRevision)
    {
        AllowNulls = false;
        DefaultExpression = "0";
        Enabled = false;
        ShouldUpdatePartials = true;
    }

    internal override UpsertArgument ToArgument()
    {
        return new RevisionArgument();
    }

    // #4526/#4528: the bigint mt_version column backs either an int IRevisioned.Version
    // or a long ILongVersioned.Version member. The bigint<->member conversion is handled
    // by DocumentRevisionBinder (read) and the closed-shape operations (write).
    protected override bool IsAcceptableMemberType(Type memberType)
        => memberType == typeof(long) || memberType == typeof(int);

    public bool ShouldSelect(DocumentMapping mapping, StorageStyle storageStyle)
    {
        if (Member != null)
        {
            return true;
        }

        return storageStyle != StorageStyle.QueryOnly && mapping.UseNumericRevisions;
    }

    public override string AlterColumnTypeSql(Table table, TableColumn changeActual)
    {
        // Non-destructive widening from the Marten 8 schema (integer) to Marten 9 (bigint).
        // Existing revision values are preserved.
        if (changeActual.Type.EqualsIgnoreCase("integer"))
        {
            return $"ALTER TABLE {table.Identifier.QualifiedName} ALTER COLUMN {Name} TYPE bigint;";
        }

        // Falling through here means the existing column is a Guid concurrency column (uuid)
        // and the user is switching to numeric revisions: drop and recreate.
        return $"ALTER TABLE {table.Identifier.QualifiedName} DROP COLUMN {Name};{AddColumnSql(table)}";
    }

    public override bool CanAlter(TableColumn actual)
    {
        return actual.Type.EqualsIgnoreCase("uuid") || actual.Type.EqualsIgnoreCase("integer");
    }

    public override void WriteMetadataInUpdateStatement(ICommandBuilder builder, DocumentSessionBase session)
    {
        builder.Append(SchemaConstants.VersionColumn);
        builder.Append(" = ");
        builder.Append(SchemaConstants.VersionColumn);
        builder.Append(" + 1");
    }
}
