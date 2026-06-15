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
/// The tolerance lives on two surfaces that must agree: the SQL side
/// (<see cref="AlterColumnTypeSql"/> no-ops a bigint actual) and the diff
/// classification side (<see cref="Equals(object)"/> reports a bigint actual as
/// a match so the column never lands in the delta) — see #4742.
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
        //
        // #4742: as of the Equals override below, a bigint actual now compares equal and
        // is sorted into ItemDelta.Matched, so this branch is no longer hit on the normal
        // path. Kept as intentional defense-in-depth — do NOT strip it as dead code: it
        // guards the SQL surface should any other call site reach AlterColumnTypeSql with
        // a bigint actual (or should the diff-side equality ever regress).
        if (changeActual.Type.EqualsIgnoreCase("bigint"))
        {
            return string.Empty;
        }

        // uuid (Guid concurrency mode) — drop and recreate as integer.
        return $"ALTER TABLE {table.Identifier.QualifiedName} DROP COLUMN {Name};{AddColumnSql(table)}";
    }

    public override bool CanAlter(TableColumn actual)
    {
        // uuid (Guid concurrency mode) routes through AlterColumnTypeSql's drop-and-recreate.
        // bigint is also reported acceptable as defense-in-depth: the Equals override below
        // normally matches a bigint actual before the diff ever asks CanAlter, but if it
        // does reach here we want the no-op ALTER path, not a column re-create with
        // potential constraint loss. See #4742.
        return actual.Type.EqualsIgnoreCase("uuid") || actual.Type.EqualsIgnoreCase("bigint");
    }

    // #4742: tolerating the wider bigint column means the schema *diff* must see it as a
    // match, not merely suppress the ALTER. Weasel's ItemDelta compares columns via
    // expected.Equals(actual) (expected being this Marten-owned column). Without this an
    // on-disk bigint lands in TableDelta.Columns.Different, which keeps the empty
    // AlterColumnTypeSql no-op for ApplyAllConfiguredChanges (fine) but classifies the
    // table as SchemaPatchDifference.Update — so AssertDatabaseMatchesConfiguration
    // (the db-assert CLI gate) throws with an empty change set. Treating a bigint actual
    // as equal routes the column to ItemDelta.Matched and the two paths agree again.
    // Deliberately asymmetric (integer tolerates bigint, not the reverse); safe because
    // ItemDelta only ever evaluates expected.Equals(actual), never the other direction.
    public override bool Equals(object? obj)
    {
        if (obj is TableColumn actual
            && string.Equals(Name, actual.Name, StringComparison.OrdinalIgnoreCase)
            && actual.RawType().EqualsIgnoreCase("bigint"))
        {
            return true;
        }

        return base.Equals(obj);
    }

    public override int GetHashCode() => base.GetHashCode();

    public override void WriteMetadataInUpdateStatement(ICommandBuilder builder, DocumentSessionBase session)
    {
        builder.Append(SchemaConstants.VersionColumn);
        builder.Append(" = ");
        builder.Append(SchemaConstants.VersionColumn);
        builder.Append(" + 1");
    }
}
