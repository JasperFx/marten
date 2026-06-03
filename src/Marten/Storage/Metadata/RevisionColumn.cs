using System;
using JasperFx.Core;
using Marten.Internal.CodeGeneration;
using Marten.Internal.Sessions;
using Marten.Schema;
using Marten.Schema.Arguments;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.Storage.Metadata;

// #4614: the mt_version column for documents using numeric revisions comes in two
// physical widths. SingleStreamProjection-projected documents implement IRevisioned
// (int) and want `integer` — the per-stream version is bounded by the stream's event
// count and was the Marten 8 default. MultiStreamProjection-projected documents
// implement ILongVersioned (long) and want `bigint` because the version is sourced
// from the global event sequence number and can exceed Int32. VersionedPolicy chooses
// the right variant when binding the column to the document type.

/// <summary>
/// The 64-bit (bigint) revision column variant. Used when the document type
/// implements <see cref="Metadata.ILongVersioned"/>. Tolerates a pre-existing
/// <c>integer</c> column on disk by widening it (non-destructively) to bigint —
/// preserves the Marten 8 → 9 upgrade path for ILongVersioned documents.
/// </summary>
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

/// <summary>
/// The 32-bit (integer) revision column variant — the Marten 8 default that
/// <see cref="VersionedPolicy"/> restores when the document type implements
/// <see cref="Metadata.IRevisioned"/>. Tolerates a pre-existing <c>bigint</c>
/// column on disk (already-on-9.x deployments are not force-narrowed); the
/// schema diff treats either width as acceptable to avoid lossy migrations.
/// </summary>
internal class RevisionColumnInt32: MetadataColumn<int>, ISelectableColumn
{
    public RevisionColumnInt32(): base(SchemaConstants.VersionColumn, x => x.CurrentRevisionInt32)
    {
        AllowNulls = false;
        DefaultExpression = "0";
        Enabled = false;
        ShouldUpdatePartials = true;
    }

    internal override UpsertArgument ToArgument()
    {
        return new RevisionArgumentInt32();
    }

    // Same dual-acceptance as the long variant: either int member (IRevisioned, natural
    // here) or long member (a user who manually wires ILongVersioned onto a SingleStream
    // doc). DocumentRevisionBinder reconciles the member width with the column width.
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
        // #4614: existing 9.x deployments already migrated mt_version to bigint when
        // RevisionColumn was the only variant. Now that IRevisioned-backed documents
        // want integer again, we deliberately tolerate the wider column on disk
        // rather than emit a lossy `USING mt_version::integer` narrowing cast — any
        // legitimate IRevisioned data is comfortably in Int32 range, but Postgres
        // refuses to verify that without scanning the table and a forced narrow risks
        // silent data loss if anything slipped through. Tolerate => no-op.
        if (changeActual.Type.EqualsIgnoreCase("bigint"))
        {
            return string.Empty;
        }

        // uuid (Guid concurrency mode) — drop and recreate as integer.
        return $"ALTER TABLE {table.Identifier.QualifiedName} DROP COLUMN {Name};{AddColumnSql(table)}";
    }

    public override bool CanAlter(TableColumn actual)
    {
        // bigint is reported as acceptable so the diff machinery routes it through
        // AlterColumnTypeSql above (which then no-ops) rather than re-creating the
        // column with potential constraint loss.
        return actual.Type.EqualsIgnoreCase("uuid") || actual.Type.EqualsIgnoreCase("bigint");
    }

    public override void WriteMetadataInUpdateStatement(ICommandBuilder builder, DocumentSessionBase session)
    {
        builder.Append(SchemaConstants.VersionColumn);
        builder.Append(" = ");
        builder.Append(SchemaConstants.VersionColumn);
        builder.Append(" + 1");
    }
}
