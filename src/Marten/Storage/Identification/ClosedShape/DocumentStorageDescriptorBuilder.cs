#nullable enable
using System.Collections.Generic;
using System.Linq;
using JasperFx.Core;
using Marten.Schema;
using Marten.Storage;

namespace Marten.Storage.Identification.ClosedShape;

/// <summary>
/// W3 spike (M1+M5): builds a <see cref="DocumentStorageDescriptor{TDoc, TId}"/>
/// from a <see cref="DocumentMapping"/>. Inspects the mapping's enabled
/// metadata columns and tenancy style and produces the binder array + SQL
/// in lockstep — column order and parameter order must agree exactly,
/// so the same builder owns both sides.
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

        var isConjoined = mapping.TenancyStyle == TenancyStyle.Conjoined;

        var upsertSql = BuildUpsertSql(mapping, writeArray, isConjoined);
        var insertSql = BuildInsertSql(mapping, writeArray, isConjoined);
        var updateSql = BuildUpdateSql(mapping, writeArray, isConjoined);

        return new DocumentStorageDescriptor<TDoc, TId>(
            identification,
            clientSideWriteBinders: clientSide,
            readBinders: readArray,
            upsertSql: upsertSql,
            insertSql: insertSql,
            updateSql: updateSql,
            isConjoined: isConjoined);
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

    private static string BuildUpsertSql<TDoc>(
        DocumentMapping mapping,
        IReadOnlyList<IDocumentMetadataBinder<TDoc>> binders,
        bool isConjoined)
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

        return $"insert into {table} ({columns.Join(", ")}) " +
               $"values ({values.Join(", ")}) " +
               $"on conflict {conflictKey} do update set {updateAssignments.Join(", ")}";
    }

    /// <summary>
    /// <c>"insert into … values (…) on conflict (…) do nothing returning id"</c>.
    /// </summary>
    private static string BuildInsertSql<TDoc>(
        DocumentMapping mapping,
        IReadOnlyList<IDocumentMetadataBinder<TDoc>> binders,
        bool isConjoined)
        where TDoc : notnull
    {
        var table = mapping.TableName.QualifiedName;
        var (columns, values) = BuildCoreColumns(binders, isConjoined);
        var conflictKey = isConjoined ? $"({Marten.Storage.Metadata.TenantIdColumn.Name}, id)" : "(id)";

        return $"insert into {table} ({columns.Join(", ")}) " +
               $"values ({values.Join(", ")}) " +
               $"on conflict {conflictKey} do nothing returning id";
    }

    /// <summary>
    /// <c>"update … set data = ?, mt_version = ?, … where id = ? [and tenant_id = ?] returning id"</c>.
    /// Parameter order: data, then each binder, then id, then tenant_id
    /// (when conjoined).
    /// </summary>
    private static string BuildUpdateSql<TDoc>(
        DocumentMapping mapping,
        IReadOnlyList<IDocumentMetadataBinder<TDoc>> binders,
        bool isConjoined)
        where TDoc : notnull
    {
        var table = mapping.TableName.QualifiedName;

        var setAssignments = new List<string>(1 + binders.Count) { "data = ?" };
        foreach (var b in binders)
        {
            setAssignments.Add($"{b.ColumnName} = {b.ValueSql}");
        }

        var whereClause = isConjoined
            ? $"where id = ? and {Marten.Storage.Metadata.TenantIdColumn.Name} = ?"
            : "where id = ?";

        return $"update {table} " +
               $"set {setAssignments.Join(", ")} " +
               $"{whereClause} " +
               $"returning id";
    }
}
