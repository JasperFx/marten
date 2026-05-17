#nullable enable
using System.Collections.Generic;
using System.Linq;
using JasperFx.Core;
using Marten.Schema;
using Marten.Storage;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// W3 spike (M1+M5+M7+M8): builds a <see cref="DocumentStorageDescriptor{TDoc, TId}"/>
/// from a <see cref="DocumentMapping"/>. Inspects the mapping's enabled
/// metadata columns, tenancy style, and concurrency mode and produces
/// the binder array + SQL in lockstep — column order and parameter order
/// must agree exactly, so the same builder owns both sides.
/// </summary>
internal static class DocumentStorageDescriptorBuilder
{
    public static DocumentStorageDescriptor<TDoc, TId> Build<TDoc, TId>(
        DocumentMapping mapping,
        IIdentification<TDoc, TId> identification)
        where TDoc : notnull
        where TId : notnull
    {
        // Two binder lists with subtly different membership rules — write
        // matches what's IN THE TABLE (every enabled metadata column),
        // read matches what's IN THE SELECT (the column.ShouldSelect
        // result, which excludes columns whose only purpose is unused
        // storage — e.g. a Version column on a mapping with no
        // [Version]-annotated member and no UseOptimisticConcurrency).
        // Order matches DocumentTable.SelectColumns: doc_type (if
        // hierarchical), version, then the rest.
        var writeBinders = new List<IDocumentMetadataBinder<TDoc>>(4);
        var readBinders = new List<IDocumentMetadataBinder<TDoc>>(4);

        DocumentVersionBinder<TDoc>? versionBinder = null;
        DocumentRevisionBinder<TDoc>? revisionBinder = null;
        var versionReadIndex = -1;
        var docTypeReadIndex = -1;
        DocumentMapping? hierarchyMapping = null;

        // M11: hierarchical mappings carry mt_doc_type — the discriminator
        // SubClassDocumentStorage filters on and the selector reads to
        // dispatch deserialization to the right subclass type.
        if (mapping.IsHierarchy())
        {
            hierarchyMapping = mapping;
            var docTypeBinder = new DocumentDocTypeBinder<TDoc>(mapping);
            writeBinders.Add(docTypeBinder);
            docTypeReadIndex = readBinders.Count;
            readBinders.Add(docTypeBinder);
        }

        if (mapping.Metadata.Revision.Enabled)
        {
            // Numeric revisions: mt_version is bigint. Revision and
            // Version columns share the same physical column name so
            // never both enabled — the validation in DocumentMapping
            // enforces it.
            revisionBinder = new DocumentRevisionBinder<TDoc>(mapping.Metadata.Revision.Member);
            writeBinders.Add(revisionBinder);

            // RevisionColumn.ShouldSelect: Member != null OR (!QueryOnly && UseNumericRevisions)
            if (mapping.Metadata.Revision.Member is not null || mapping.UseNumericRevisions)
            {
                versionReadIndex = readBinders.Count;
                readBinders.Add(revisionBinder);
            }
        }
        else if (mapping.Metadata.Version.Enabled)
        {
            versionBinder = new DocumentVersionBinder<TDoc>(mapping.Metadata.Version.Member);
            writeBinders.Add(versionBinder);

            // VersionColumn.ShouldSelect: Member != null OR (!QueryOnly && UseOptimisticConcurrency)
            if (mapping.Metadata.Version.Member is not null || mapping.UseOptimisticConcurrency)
            {
                versionReadIndex = readBinders.Count;
                readBinders.Add(versionBinder);
            }
        }

        if (mapping.Metadata.DotNetType.Enabled)
        {
            // DotNetTypeColumn is IEventTableColumn only (not
            // ISelectableColumn), so it's in the table but NOT in the
            // document SELECT projection — write only.
            var binder = new DocumentDotNetTypeBinder<TDoc>();
            writeBinders.Add(binder);
        }

        // tenant_id column. Conjoined mappings carry the column in the
        // table and DocumentTable.SelectColumns surfaces it for read when
        // Member != null. The column position in the SELECT lands
        // immediately after id/data/doc_type/version (it's the first
        // column in Columns natural order after id/data are stripped),
        // so the binder goes BEFORE last_modified in readBinders. The
        // write side is bound directly inline by the storage operation,
        // so it does NOT participate in writeBinders.
        if (mapping.TenancyStyle == TenancyStyle.Conjoined
            && mapping.Metadata.TenantId.Member is not null)
        {
            readBinders.Add(new DocumentTenantIdBinder<TDoc>(mapping.Metadata.TenantId.Member));
        }

        if (mapping.Metadata.LastModified.Enabled)
        {
            // LastModifiedColumn.ShouldSelect: Member != null.
            var binder = new DocumentLastModifiedBinder<TDoc>(mapping.Metadata.LastModified.Member);
            writeBinders.Add(binder);
            if (mapping.Metadata.LastModified.Member is not null)
            {
                readBinders.Add(binder);
            }
        }

        // Session-derived metadata columns: correlation_id, causation_id,
        // last_modified_by, headers. Each column gets a write slot when
        // enabled (the value comes from IMartenSession on every write,
        // not from the document) and a read slot only when the mapping
        // has a member to project onto (ShouldSelect=EnabledWithMember()
        // on the underlying MetadataColumn).
        if (mapping.Metadata.CorrelationId.Enabled)
        {
            var binder = new DocumentCorrelationIdBinder<TDoc>(mapping.Metadata.CorrelationId.Member);
            writeBinders.Add(binder);
            if (mapping.Metadata.CorrelationId.Member is not null)
            {
                readBinders.Add(binder);
            }
        }

        if (mapping.Metadata.CausationId.Enabled)
        {
            var binder = new DocumentCausationIdBinder<TDoc>(mapping.Metadata.CausationId.Member);
            writeBinders.Add(binder);
            if (mapping.Metadata.CausationId.Member is not null)
            {
                readBinders.Add(binder);
            }
        }

        if (mapping.Metadata.LastModifiedBy.Enabled)
        {
            var binder = new DocumentLastModifiedByBinder<TDoc>(mapping.Metadata.LastModifiedBy.Member);
            writeBinders.Add(binder);
            if (mapping.Metadata.LastModifiedBy.Member is not null)
            {
                readBinders.Add(binder);
            }
        }

        if (mapping.Metadata.Headers.Enabled)
        {
            // HeadersColumn is NOT ISelectableColumn — the column exists
            // for MetadataForAsync but is excluded from the document
            // SELECT projection. The Headers member gets its value from
            // the JSON data column instead (ApplyToDocument projects the
            // session's Headers dict onto the document before
            // serialization). Hence write-only here.
            var binder = new DocumentHeadersBinder<TDoc>(mapping.Metadata.Headers.Member);
            writeBinders.Add(binder);
        }

        // M10: duplicated fields contribute write-only columns to the
        // table (they're not ISelectableColumn — the canonical value is
        // deserialized from the data JSON column). Append before
        // soft-delete columns since DocumentTable.AddColumn orders
        // duplicated fields before mt_deleted / mt_deleted_at.
        if (mapping.DuplicatedFields.Count > 0)
        {
            var enumStorage = mapping.StoreOptions.Advanced.DuplicatedFieldEnumStorage;
            foreach (var field in mapping.DuplicatedFields)
            {
                if (field.OnlyForSearching) continue;
                writeBinders.Add(new DocumentDuplicatedFieldBinder<TDoc>(field, enumStorage));
            }
        }

        // M9: soft delete adds two columns to the table. Each save writes
        // the defaults (false, null) so re-saving a soft-deleted document
        // undeletes it — same observable behavior as the codegen path's
        // UpsertFunction. The actual soft-delete operation lives in the
        // inherited DocumentStorage<T, TId>.DeleteFragment.
        // Gate on DeleteStyle, not Metadata.IsSoftDeleted.Enabled —
        // MetadataColumn<T>.Enabled defaults to true regardless of the
        // mapping's delete style.
        if (mapping.DeleteStyle == DeleteStyle.SoftDelete)
        {
            var isDeleted = new DocumentSoftDeletedBinder<TDoc>(mapping.Metadata.IsSoftDeleted.Member);
            writeBinders.Add(isDeleted);
            if (mapping.Metadata.IsSoftDeleted.Member is not null)
            {
                readBinders.Add(isDeleted);
            }

            var deletedAt = new DocumentSoftDeletedAtBinder<TDoc>(mapping.Metadata.SoftDeletedAt.Member);
            writeBinders.Add(deletedAt);
            if (mapping.Metadata.SoftDeletedAt.Member is not null)
            {
                readBinders.Add(deletedAt);
            }
        }

        var writeArray = writeBinders.ToArray();
        var readArray = readBinders.ToArray();
        var clientSide = writeArray.Where(b => !b.IsServerSide).ToArray();

        var isConjoined = mapping.TenancyStyle == TenancyStyle.Conjoined;
        var concurrencyMode = mapping.UseNumericRevisions
            ? ConcurrencyMode.Numeric
            : mapping.UseOptimisticConcurrency
                ? ConcurrencyMode.Optimistic
                : ConcurrencyMode.Off;

        // Partition PK columns — anything in the table's PK that isn't id
        // or tenant_id (those are bound inline by the operation). Match
        // by column name to a write binder so the operation can rebind
        // it for the WHERE clause. Order matches the table's column order.
        var partitionPkColumns = mapping.Schema.Table.Columns
            .Where(c => c.IsPrimaryKey)
            .Select(c => c.Name)
            .Where(name => name != "id" && name != Marten.Storage.Metadata.TenantIdColumn.Name)
            .ToArray();
        var partitionPkBinders = partitionPkColumns
            .Select(name => clientSide.FirstOrDefault(b => b.ColumnName == name))
            .Where(b => b is not null)
            .Select(b => b!)
            .ToArray();

        var upsertSql = BuildUpsertSql(mapping, writeArray, partitionPkBinders, isConjoined, concurrencyMode);
        var insertSql = BuildInsertSql(mapping, writeArray, isConjoined, concurrencyMode);
        var updateSql = BuildUpdateSql(mapping, writeArray, partitionPkBinders, isConjoined, concurrencyMode);
        var overwriteSql = BuildOverwriteSql(mapping, writeArray, partitionPkBinders, isConjoined, concurrencyMode);

        return new DocumentStorageDescriptor<TDoc, TId>(
            identification,
            clientSideWriteBinders: clientSide,
            writeBinders: writeArray,
            readBinders: readArray,
            upsertSql: upsertSql,
            insertSql: insertSql,
            updateSql: updateSql,
            overwriteSql: overwriteSql,
            isConjoined: isConjoined,
            concurrencyMode: concurrencyMode,
            versionBinder: versionBinder,
            revisionBinder: revisionBinder,
            versionReadIndex: versionReadIndex,
            hierarchyMapping: hierarchyMapping,
            docTypeReadIndex: docTypeReadIndex,
            tableName: mapping.TableName.Name,
            partitionPkBinders: partitionPkBinders);
    }

    /// <summary>
    /// Builds the core column + value lists. For conjoined the tenant id
    /// is prepended as the first column / parameter slot. Under
    /// <see cref="ConcurrencyMode.Numeric"/> the revision binder emits a
    /// <c>CASE WHEN ? = 0 THEN 1 ELSE ? END</c> expression so a caller
    /// passing <c>Revision = 0</c> ends up inserting <c>1</c> (the
    /// initial revision) rather than literal zero.
    /// </summary>
    private static (List<string> Columns, List<string> Values) BuildCoreColumns<TDoc>(
        IReadOnlyList<IDocumentMetadataBinder<TDoc>> binders,
        bool isConjoined,
        ConcurrencyMode mode)
        where TDoc : notnull
    {
        var capacity = (isConjoined ? 3 : 2) + binders.Count;
        var columns = new List<string>(capacity);
        var values = new List<string>(capacity);

        if (isConjoined)
        {
            columns.Add(Marten.Storage.Metadata.TenantIdColumn.Name);
            values.Add("?");
        }
        columns.Add("id");
        values.Add("?");
        columns.Add("data");
        values.Add("?");

        foreach (var b in binders)
        {
            columns.Add(b.ColumnName);
            if (mode == ConcurrencyMode.Numeric && b is DocumentRevisionBinder<TDoc>)
            {
                // Two ? slots — the operation binds Revision to both.
                values.Add("CASE WHEN ? = 0 THEN 1 ELSE ? END");
            }
            else
            {
                values.Add(b.ValueSql);
            }
        }

        return (columns, values);
    }

    /// <summary>
    /// What the operation returns from <c>RETURNING</c> — <c>id</c> when
    /// no concurrency tracking; <c>mt_version</c> when optimistic or
    /// numeric so the operation can validate + write the version back.
    /// </summary>
    private static string ReturningColumn(ConcurrencyMode mode)
        => mode == ConcurrencyMode.Off
            ? "id"
            : Marten.Schema.SchemaConstants.VersionColumn;

    private static string BuildUpsertSql<TDoc>(
        DocumentMapping mapping,
        IReadOnlyList<IDocumentMetadataBinder<TDoc>> binders,
        IReadOnlyList<IDocumentMetadataBinder<TDoc>> partitionPkBinders,
        bool isConjoined,
        ConcurrencyMode mode)
        where TDoc : notnull
        => BuildUpsertOrOverwriteSql(mapping, binders, partitionPkBinders, isConjoined, mode, includeConcurrencyGuard: mode != ConcurrencyMode.Off);

    private static string BuildUpsertOrOverwriteSql<TDoc>(
        DocumentMapping mapping,
        IReadOnlyList<IDocumentMetadataBinder<TDoc>> binders,
        IReadOnlyList<IDocumentMetadataBinder<TDoc>> partitionPkBinders,
        bool isConjoined,
        ConcurrencyMode mode,
        bool includeConcurrencyGuard)
        where TDoc : notnull
    {
        var table = mapping.TableName.QualifiedName;
        var (columns, values) = BuildCoreColumns(binders, isConjoined, mode);
        var versionColumn = Marten.Schema.SchemaConstants.VersionColumn;
        var conflictKey = BuildConflictKey(mapping);

        var updateAssignments = new List<string>(1 + binders.Count) { "data = excluded.data" };
        foreach (var b in binders)
        {
            if (mode == ConcurrencyMode.Numeric && b is DocumentRevisionBinder<TDoc>)
            {
                // Auto-increment when the caller passed Revision = 0;
                // otherwise use the caller-supplied revision. Re-bind
                // raw Revision into 2 fresh ? slots — the CASE in the
                // INSERT VALUES already clobbered excluded.mt_version
                // so we can't read the raw value from there anymore.
                updateAssignments.Add($"{versionColumn} = CASE WHEN ? = 0 THEN {table}.{versionColumn} + 1 ELSE ? END");
            }
            else
            {
                updateAssignments.Add($"{b.ColumnName} = excluded.{b.ColumnName}");
            }
        }

        // Concurrency guard inside ON CONFLICT DO UPDATE:
        //   Optimistic: table.mt_version (uuid) = expected (bound)
        //   Numeric:    auto-increment (? = 0) always wins; explicit
        //               revisions only when > current.
        string conflictWhere = string.Empty;
        if (includeConcurrencyGuard)
        {
            conflictWhere = mode switch
            {
                ConcurrencyMode.Optimistic => $" where {table}.{versionColumn} = ?",
                ConcurrencyMode.Numeric => $" where ? = 0 or {table}.{versionColumn} < ?",
                _ => string.Empty
            };
        }

        return $"insert into {table} ({columns.Join(", ")}) " +
               $"values ({values.Join(", ")}) " +
               $"on conflict {conflictKey} do update set {updateAssignments.Join(", ")}{conflictWhere} " +
               $"returning {ReturningColumn(mode)}";
    }

    /// <summary>
    /// <c>"insert into … values (…) on conflict (…) do nothing returning {id|mt_version}"</c>.
    /// </summary>
    private static string BuildInsertSql<TDoc>(
        DocumentMapping mapping,
        IReadOnlyList<IDocumentMetadataBinder<TDoc>> binders,
        bool isConjoined,
        ConcurrencyMode mode)
        where TDoc : notnull
    {
        var table = mapping.TableName.QualifiedName;
        var (columns, values) = BuildCoreColumns(binders, isConjoined, mode);
        var conflictKey = BuildConflictKey(mapping);

        return $"insert into {table} ({columns.Join(", ")}) " +
               $"values ({values.Join(", ")}) " +
               $"on conflict {conflictKey} do nothing returning {ReturningColumn(mode)}";
    }

    /// <summary>
    /// ON CONFLICT key — matches the document table's actual primary
    /// key. For unpartitioned tables that's <c>(id)</c> or
    /// <c>(tenant_id, id)</c> for conjoined; partitioned tables (e.g.
    /// list-partitioned by <c>mt_deleted</c>) add the partition columns
    /// to the PK and the conflict key has to follow. Mirrors the
    /// <c>_primaryKeyFields = table.Columns.Where(IsPrimaryKey).Select(Name)</c>
    /// derivation that the codegen <c>UpsertFunction</c> used.
    /// </summary>
    private static string BuildConflictKey(DocumentMapping mapping)
    {
        var pkColumns = mapping.Schema.Table.Columns
            .Where(c => c.IsPrimaryKey)
            .Select(c => c.Name)
            .ToArray();
        return $"({pkColumns.Join(", ")})";
    }

    /// <summary>
    /// Update SQL — parameter order: data, then each binder, then id,
    /// then tenant_id (when conjoined), then concurrency-guard params
    /// (optimistic = 1 expected version; numeric = 0 since the
    /// auto/explicit logic is baked into the SET / WHERE via CASE).
    /// </summary>
    private static string BuildUpdateSql<TDoc>(
        DocumentMapping mapping,
        IReadOnlyList<IDocumentMetadataBinder<TDoc>> binders,
        IReadOnlyList<IDocumentMetadataBinder<TDoc>> partitionPkBinders,
        bool isConjoined,
        ConcurrencyMode mode)
        where TDoc : notnull
    {
        var table = mapping.TableName.QualifiedName;
        var versionColumn = Marten.Schema.SchemaConstants.VersionColumn;

        var setAssignments = new List<string>(1 + binders.Count) { "data = ?" };
        foreach (var b in binders)
        {
            if (mode == ConcurrencyMode.Numeric && b is DocumentRevisionBinder<TDoc>)
            {
                // CASE WHEN ? = 0 THEN mt_version + 1 ELSE ? END
                setAssignments.Add($"{versionColumn} = CASE WHEN ? = 0 THEN {table}.{versionColumn} + 1 ELSE ? END");
            }
            else
            {
                setAssignments.Add($"{b.ColumnName} = {b.ValueSql}");
            }
        }

        var whereClauses = new List<string>(3 + partitionPkBinders.Count) { "id = ?" };
        if (isConjoined)
        {
            whereClauses.Add($"{Marten.Storage.Metadata.TenantIdColumn.Name} = ?");
        }
        // Bug #4223: partitioned tables that include the partition column
        // in the PK need WHERE filters on those columns too — otherwise
        // UPDATE ... WHERE id = ? targets every partition row for that id
        // and the SET clause produces a PK violation as the second row
        // collides with the first.
        foreach (var pk in partitionPkBinders)
        {
            whereClauses.Add($"{pk.ColumnName} = ?");
        }
        if (mode == ConcurrencyMode.Optimistic)
        {
            whereClauses.Add($"{versionColumn} = ?");
        }
        else if (mode == ConcurrencyMode.Numeric)
        {
            // Auto-increment (?  = 0) always wins; explicit revisions
            // only when strictly greater than the current value.
            whereClauses.Add($"(? = 0 or {table}.{versionColumn} < ?)");
        }

        return $"update {table} " +
               $"set {setAssignments.Join(", ")} " +
               $"where {whereClauses.Join(" and ")} " +
               $"returning {ReturningColumn(mode)}";
    }

    /// <summary>
    /// Overwrite is "upsert without the concurrency guard." Same SQL as
    /// upsert except the trailing version filter on the ON CONFLICT
    /// branch is dropped — the caller has explicitly asked to bypass
    /// the check.
    /// </summary>
    private static string BuildOverwriteSql<TDoc>(
        DocumentMapping mapping,
        IReadOnlyList<IDocumentMetadataBinder<TDoc>> binders,
        IReadOnlyList<IDocumentMetadataBinder<TDoc>> partitionPkBinders,
        bool isConjoined,
        ConcurrencyMode mode)
        where TDoc : notnull
        => BuildUpsertOrOverwriteSql(mapping, binders, partitionPkBinders, isConjoined, mode, includeConcurrencyGuard: false);
}
