#nullable enable
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Internal.CodeGeneration;
using Marten.Internal.Storage;
using Marten.Schema;
using Marten.Storage;
using Npgsql;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// W3 spike (M16): closed-shape <see cref="BulkLoader{T, TId}"/> built
/// from a <see cref="DocumentStorageDescriptor{TDoc, TId}"/>. Streams
/// rows through PostgreSQL's COPY binary protocol — same wire format
/// the codegen-emitted bulk loader uses, just constructed at runtime
/// from the descriptor's column list instead of an emitted subclass.
/// </summary>
internal sealed class ClosedShapeBulkLoader<TDoc, TId>: BulkLoader<TDoc, TId>
    where TDoc : notnull
    where TId : notnull
{
    private readonly DocumentStorageDescriptor<TDoc, TId> _descriptor;
    private readonly DocumentMapping _mapping;
    private readonly string _mainSql;
    private readonly string _tempTableName;
    private readonly string _tempSql;

    public ClosedShapeBulkLoader(IDocumentStorage<TDoc, TId> storage,
        DocumentStorageDescriptor<TDoc, TId> descriptor,
        DocumentMapping mapping)
        : base(storage)
    {
        _descriptor = descriptor;
        _mapping = mapping;
        _tempTableName = mapping.TableName.Name + "_temp";

        _mainSql = $"COPY {mapping.TableName.QualifiedName}({ColumnList()}) FROM STDIN BINARY";
        _tempSql = $"COPY {_tempTableName}({ColumnList()}) FROM STDIN BINARY";
    }

    public override string MainLoaderSql() => _mainSql;

    public override string TempLoaderSql() => _tempSql;

    public override string CreateTempTableForCopying()
        => $"create temporary table {_tempTableName} as select * from {_mapping.TableName.QualifiedName} limit 0;";

    public override string CopyNewDocumentsFromTempTable()
        => $"insert into {_mapping.TableName.QualifiedName} (select * from {_tempTableName}) " +
           $"on conflict {ConflictKey()} do nothing";

    public override string OverwriteDuplicatesFromTempTable()
    {
        // SET data, plus every write binder's column — including
        // server-side ones (LastModified) since the COPY path carries
        // client-computed values for them through the temp table.
        var assignments = new System.Collections.Generic.List<string>(8) { "data = excluded.data" };
        foreach (var b in _descriptor.WriteBinders)
        {
            assignments.Add($"{b.ColumnName} = excluded.{b.ColumnName}");
        }

        return
            $"insert into {_mapping.TableName.QualifiedName} (select * from {_tempTableName}) " +
            $"on conflict {ConflictKey()} do update set {assignments.Join(", ")}";
    }

    private string ConflictKey()
    {
        // Mirrors the partition-aware key in
        // DocumentStorageDescriptorBuilder.BuildConflictKey — uses the
        // table's actual primary key so list-partitioned mappings (PK
        // includes mt_deleted etc.) work too.
        var pkColumns = _mapping.Schema.Table.Columns
            .Where(c => c.IsPrimaryKey)
            .Select(c => c.Name)
            .ToArray();
        return $"({pkColumns.Join(", ")})";
    }

    public override async Task LoadRowAsync(NpgsqlBinaryImporter writer, TDoc document, Tenant tenant,
        ISerializer serializer, CancellationToken cancellation)
    {
        // Column order matches ColumnList(): [tenant_id], id, data, binders...
        if (_descriptor.IsConjoined)
        {
            await writer.WriteAsync(tenant.TenantId, NpgsqlDbType.Varchar, cancellation).ConfigureAwait(false);
        }

        var id = _descriptor.Identification.Identity(document);
        var rawId = _descriptor.Identification.ToRawSqlValue(id);
        var idDbType = PostgresqlProvider.Instance.ToParameterType(_descriptor.Identification.RawSqlType);
        await writer.WriteAsync(rawId, idDbType, cancellation).ConfigureAwait(false);

        // data column — serialize to JSON via the configured ISerializer.
        var json = serializer.ToJson(document);
        await writer.WriteAsync(json, NpgsqlDbType.Jsonb, cancellation).ConfigureAwait(false);

        // Each metadata binder writes its column value.
        foreach (var binder in _descriptor.WriteBinders)
        {
            await binder.WriteToBulkAsync(writer, document, serializer, cancellation).ConfigureAwait(false);
        }
    }

    private string ColumnList()
    {
        var columns = new System.Collections.Generic.List<string>(4 + _descriptor.WriteBinders.Length);
        if (_descriptor.IsConjoined)
        {
            columns.Add($"\"{Marten.Storage.Metadata.TenantIdColumn.Name}\"");
        }
        columns.Add("\"id\"");
        columns.Add("\"data\"");
        foreach (var b in _descriptor.WriteBinders)
        {
            columns.Add($"\"{b.ColumnName}\"");
        }
        return columns.Join(", ");
    }
}
