#nullable enable
using System.Collections.Generic;
using System.Linq;
using JasperFx.Core;
using Marten.Schema;

namespace Marten.Storage.Identification.ClosedShape;

/// <summary>
/// W3 spike (M1): builds a <see cref="DocumentStorageDescriptor{TDoc, TId}"/>
/// from a <see cref="DocumentMapping"/>. Inspects the mapping's enabled
/// metadata columns and produces the binder array + SQL in lockstep —
/// column order and parameter order must agree exactly, so the same
/// builder owns both sides.
/// </summary>
/// <remarks>
/// Closed-shape equivalent of <c>PostgresEventStoreDialect.BuildRichDescriptor</c>
/// from W4. The M1 spike scope: <c>mt_version</c>, <c>mt_dotnet_type</c>,
/// <c>mt_last_modified</c>. Other metadata columns (tenant, soft delete,
/// headers, causation/correlation/username, duplicated fields) land in
/// subsequent milestones.
/// </remarks>
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

        if (mapping.Metadata.Version.Enabled)
        {
            var binder = new DocumentVersionBinder<TDoc>(mapping.Metadata.Version.Member);
            writeBinders.Add(binder);

            // VersionColumn.ShouldSelect: Member != null OR (!QueryOnly && UseOptimisticConcurrency)
            if (mapping.Metadata.Version.Member is not null || mapping.UseOptimisticConcurrency)
            {
                readBinders.Add(binder);
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

        var sql = BuildUpsertSql(mapping, writeArray);

        return new DocumentStorageDescriptor<TDoc, TId>(
            identification,
            clientSideWriteBinders: clientSide,
            readBinders: readArray,
            upsertSql: sql);
    }

    private static string BuildUpsertSql<TDoc>(
        DocumentMapping mapping,
        IReadOnlyList<IDocumentMetadataBinder<TDoc>> binders)
        where TDoc : notnull
    {
        var table = mapping.TableName.QualifiedName;

        // Column list: id, data, then each binder's ColumnName in order.
        var columnNames = new List<string>(2 + binders.Count) { "id", "data" };
        foreach (var b in binders) columnNames.Add(b.ColumnName);

        // VALUES list: ?, ? for id+data, then either ? (client-side) or
        // the binder's ValueSql literal (server-side).
        var valueSlots = new List<string>(2 + binders.Count) { "?", "?" };
        foreach (var b in binders) valueSlots.Add(b.ValueSql);

        // ON CONFLICT UPDATE clause: refresh data + every binder's column
        // from EXCLUDED.
        var updateAssignments = new List<string>(1 + binders.Count) { "data = excluded.data" };
        foreach (var b in binders)
        {
            updateAssignments.Add($"{b.ColumnName} = excluded.{b.ColumnName}");
        }

        return $"insert into {table} ({columnNames.Join(", ")}) " +
               $"values ({valueSlots.Join(", ")}) " +
               $"on conflict (id) do update set {updateAssignments.Join(", ")}";
    }
}
