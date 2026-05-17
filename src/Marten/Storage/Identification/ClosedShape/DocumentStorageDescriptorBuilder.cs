#nullable enable
using System.Collections.Generic;
using System.Linq;
using JasperFx.Core;
using Marten.Schema;
using Marten.Storage;

namespace Marten.Storage.Identification.ClosedShape;

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
        var writeBinders = new List<IDocumentMetadataBinder<TDoc>>(4);
        var readBinders = new List<IDocumentMetadataBinder<TDoc>>(4);

        DocumentVersionBinder<TDoc>? versionBinder = null;
        DocumentRevisionBinder<TDoc>? revisionBinder = null;
        var versionReadOrdinal = -1;

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
                versionReadOrdinal = 2 + readBinders.Count;
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
                // Column ordinal inside the SELECT projection: id (0),
                // data (1), then read binders in append order starting at 2.
                versionReadOrdinal = 2 + readBinders.Count;
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

        var writeArray = writeBinders.ToArray();
        var readArray = readBinders.ToArray();
        var clientSide = writeArray.Where(b => !b.IsServerSide).ToArray();

        var isConjoined = mapping.TenancyStyle == TenancyStyle.Conjoined;
        var concurrencyMode = mapping.UseNumericRevisions
            ? ConcurrencyMode.Numeric
            : mapping.UseOptimisticConcurrency
                ? ConcurrencyMode.Optimistic
                : ConcurrencyMode.Off;

        var upsertSql = BuildUpsertSql(mapping, writeArray, isConjoined, concurrencyMode);
        var insertSql = BuildInsertSql(mapping, writeArray, isConjoined, concurrencyMode);
        var updateSql = BuildUpdateSql(mapping, writeArray, isConjoined, concurrencyMode);
        var overwriteSql = BuildOverwriteSql(mapping, writeArray, isConjoined, concurrencyMode);

        return new DocumentStorageDescriptor<TDoc, TId>(
            identification,
            clientSideWriteBinders: clientSide,
            readBinders: readArray,
            upsertSql: upsertSql,
            insertSql: insertSql,
            updateSql: updateSql,
            overwriteSql: overwriteSql,
            isConjoined: isConjoined,
            concurrencyMode: concurrencyMode,
            versionBinder: versionBinder,
            revisionBinder: revisionBinder,
            versionReadOrdinal: versionReadOrdinal);
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
        bool isConjoined,
        ConcurrencyMode mode)
        where TDoc : notnull
        => BuildUpsertOrOverwriteSql(mapping, binders, isConjoined, mode, includeConcurrencyGuard: mode != ConcurrencyMode.Off);

    private static string BuildUpsertOrOverwriteSql<TDoc>(
        DocumentMapping mapping,
        IReadOnlyList<IDocumentMetadataBinder<TDoc>> binders,
        bool isConjoined,
        ConcurrencyMode mode,
        bool includeConcurrencyGuard)
        where TDoc : notnull
    {
        var table = mapping.TableName.QualifiedName;
        var (columns, values) = BuildCoreColumns(binders, isConjoined, mode);
        var versionColumn = Marten.Schema.SchemaConstants.VersionColumn;

        // ON CONFLICT key: (tenant_id, id) when conjoined, (id) otherwise.
        var conflictKey = isConjoined ? $"({Marten.Storage.Metadata.TenantIdColumn.Name}, id)" : "(id)";

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
        var conflictKey = isConjoined ? $"({Marten.Storage.Metadata.TenantIdColumn.Name}, id)" : "(id)";

        return $"insert into {table} ({columns.Join(", ")}) " +
               $"values ({values.Join(", ")}) " +
               $"on conflict {conflictKey} do nothing returning {ReturningColumn(mode)}";
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

        var whereClauses = new List<string>(3) { "id = ?" };
        if (isConjoined)
        {
            whereClauses.Add($"{Marten.Storage.Metadata.TenantIdColumn.Name} = ?");
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
        bool isConjoined,
        ConcurrencyMode mode)
        where TDoc : notnull
        => BuildUpsertOrOverwriteSql(mapping, binders, isConjoined, mode, includeConcurrencyGuard: false);
}
