#nullable enable
using System.Collections.Generic;
using System.Linq;
using JasperFx.Core;
using Marten.Schema;
using Marten.Storage;

namespace Marten.Storage.Identification.ClosedShape;

/// <summary>
/// W3 spike (M1+M5+M7): builds a <see cref="DocumentStorageDescriptor{TDoc, TId}"/>
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
        var versionReadOrdinal = -1;

        if (mapping.Metadata.Version.Enabled)
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
        var concurrencyMode = mapping.UseOptimisticConcurrency
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
            versionReadOrdinal: versionReadOrdinal);
    }

    /// <summary>
    /// Builds the core column + value lists. For conjoined the tenant id
    /// is prepended as the first column / parameter slot.
    /// </summary>
    private static (List<string> Columns, List<string> Values) BuildCoreColumns<TDoc>(
        IReadOnlyList<IDocumentMetadataBinder<TDoc>> binders, bool isConjoined)
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
            values.Add(b.ValueSql);
        }

        return (columns, values);
    }

    /// <summary>
    /// What the operation returns from <c>RETURNING</c> — <c>id</c> when
    /// no concurrency tracking, <c>mt_version</c> when optimistic so the
    /// operation can validate + write back the version.
    /// </summary>
    private static string ReturningColumn(ConcurrencyMode mode)
        => mode == ConcurrencyMode.Optimistic
            ? Marten.Schema.SchemaConstants.VersionColumn
            : "id";

    private static string BuildUpsertSql<TDoc>(
        DocumentMapping mapping,
        IReadOnlyList<IDocumentMetadataBinder<TDoc>> binders,
        bool isConjoined,
        ConcurrencyMode mode)
        where TDoc : notnull
        => BuildUpsertOrOverwriteSql(mapping, binders, isConjoined, mode, includeVersionWhere: mode == ConcurrencyMode.Optimistic);

    private static string BuildUpsertOrOverwriteSql<TDoc>(
        DocumentMapping mapping,
        IReadOnlyList<IDocumentMetadataBinder<TDoc>> binders,
        bool isConjoined,
        ConcurrencyMode mode,
        bool includeVersionWhere)
        where TDoc : notnull
    {
        var table = mapping.TableName.QualifiedName;
        var (columns, values) = BuildCoreColumns(binders, isConjoined);

        // ON CONFLICT key: (tenant_id, id) when conjoined, (id) otherwise.
        var conflictKey = isConjoined ? $"({Marten.Storage.Metadata.TenantIdColumn.Name}, id)" : "(id)";

        var updateAssignments = new List<string>(1 + binders.Count) { "data = excluded.data" };
        foreach (var b in binders)
        {
            updateAssignments.Add($"{b.ColumnName} = excluded.{b.ColumnName}");
        }

        // Optimistic: the ON CONFLICT DO UPDATE only fires when the row's
        // current mt_version equals the expected version supplied by the
        // caller (sourced from session.Versions). If no match, no rows
        // updated → no RETURNING → ConcurrencyException at postprocess.
        // Overwrite drops this filter so the write always wins.
        var conflictWhere = includeVersionWhere
            ? $" where {table}.{Marten.Schema.SchemaConstants.VersionColumn} = ?"
            : string.Empty;

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
        var (columns, values) = BuildCoreColumns(binders, isConjoined);
        var conflictKey = isConjoined ? $"({Marten.Storage.Metadata.TenantIdColumn.Name}, id)" : "(id)";

        return $"insert into {table} ({columns.Join(", ")}) " +
               $"values ({values.Join(", ")}) " +
               $"on conflict {conflictKey} do nothing returning {ReturningColumn(mode)}";
    }

    /// <summary>
    /// <c>"update … set data = ?, mt_version = ?, … where id = ? [and tenant_id = ?] [and mt_version = ?] returning {id|mt_version}"</c>.
    /// Parameter order: data, then each binder, then id, then tenant_id
    /// (when conjoined), then expected version (when optimistic).
    /// </summary>
    private static string BuildUpdateSql<TDoc>(
        DocumentMapping mapping,
        IReadOnlyList<IDocumentMetadataBinder<TDoc>> binders,
        bool isConjoined,
        ConcurrencyMode mode)
        where TDoc : notnull
    {
        var table = mapping.TableName.QualifiedName;

        var setAssignments = new List<string>(1 + binders.Count) { "data = ?" };
        foreach (var b in binders)
        {
            setAssignments.Add($"{b.ColumnName} = {b.ValueSql}");
        }

        var whereClauses = new List<string>(3) { "id = ?" };
        if (isConjoined)
        {
            whereClauses.Add($"{Marten.Storage.Metadata.TenantIdColumn.Name} = ?");
        }
        if (mode == ConcurrencyMode.Optimistic)
        {
            whereClauses.Add($"{Marten.Schema.SchemaConstants.VersionColumn} = ?");
        }

        return $"update {table} " +
               $"set {setAssignments.Join(", ")} " +
               $"where {whereClauses.Join(" and ")} " +
               $"returning {ReturningColumn(mode)}";
    }

    /// <summary>
    /// Overwrite is "upsert without the optimistic version guard."
    /// Same SQL as upsert except the trailing <c>where mt_version = ?</c>
    /// is dropped. When <see cref="ConcurrencyMode"/> is <c>Off</c>,
    /// overwrite SQL is byte-identical to upsert SQL (kept separate to
    /// preserve a clean 1:1 mapping with the codegen path's
    /// <c>OverwriteFunction</c>).
    /// </summary>
    private static string BuildOverwriteSql<TDoc>(
        DocumentMapping mapping,
        IReadOnlyList<IDocumentMetadataBinder<TDoc>> binders,
        bool isConjoined,
        ConcurrencyMode mode)
        where TDoc : notnull
        => BuildUpsertOrOverwriteSql(mapping, binders, isConjoined, mode, includeVersionWhere: false);
}
