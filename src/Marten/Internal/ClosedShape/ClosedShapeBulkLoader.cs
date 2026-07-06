#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
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
    private readonly bool _needsExpectedVersion;
    private readonly string _mainSql;
    private readonly string _tempTableName;
    private readonly string _tempSql;
    // FEC-compiled accessors for the [Version] / [Revision] member used
    // to populate mt_expected_version in the temp table. Cached once;
    // null when the mapping has no annotated member.
    private readonly Func<TDoc, Guid>? _versionGetter;
    private readonly Func<TDoc, long>? _revisionGetter;

    public ClosedShapeBulkLoader(IDocumentStorage<TDoc, TId> storage,
        DocumentStorageDescriptor<TDoc, TId> descriptor,
        DocumentMapping mapping)
        : base(storage)
    {
        _descriptor = descriptor;
        _mapping = mapping;
        _tempTableName = mapping.TableName.Name + "_temp";
        // Mirrors BulkLoaderBuilder.needsExpectedVersion(): only mappings
        // with versioning enabled can use OverwriteIfVersionMatches; for
        // everything else the temp table doesn't need the extra column
        // and TempLoaderSql is the same as MainLoaderSql.
        _needsExpectedVersion = mapping.Metadata.Version.Enabled
            || mapping.Metadata.Revision.Enabled
            || mapping.UseOptimisticConcurrency
            || mapping.UseNumericRevisions;

        _mainSql = $"COPY {mapping.TableName.QualifiedName}({ColumnList(includeExpectedVersion: false)}) FROM STDIN BINARY";
        _tempSql = $"COPY {_tempTableName}({ColumnList(includeExpectedVersion: _needsExpectedVersion)}) FROM STDIN BINARY";

        if (_needsExpectedVersion)
        {
            if (mapping.UseNumericRevisions || mapping.Metadata.Revision.Enabled)
            {
                if (mapping.Metadata.Revision.Member is not null)
                {
                    _revisionGetter = LambdaBuilder.Getter<TDoc, long>(mapping.Metadata.Revision.Member);
                }
            }
            else if (mapping.Metadata.Version.Member is not null)
            {
                _versionGetter = LambdaBuilder.Getter<TDoc, Guid>(mapping.Metadata.Version.Member);
            }
        }
    }

    public override string MainLoaderSql() => _mainSql;

    public override string TempLoaderSql() => _tempSql;

    public override string CreateTempTableForCopying()
    {
        if (!_needsExpectedVersion)
        {
            return $"create temporary table {_tempTableName} as select * from {_mapping.TableName.QualifiedName} limit 0;";
        }

        // Numeric revisions are stored as bigint on the main table —
        // mt_version's type follows that. Match the source column's
        // postgres type so source.mt_expected_version compares cleanly.
        var expectedType = _mapping.UseNumericRevisions || _mapping.Metadata.Revision.Enabled
            ? "bigint"
            : "uuid";
        return
            $"create temporary table {_tempTableName} as select * from {_mapping.TableName.QualifiedName} limit 0;" +
            $"alter table {_tempTableName} add column \"{Marten.Schema.SchemaConstants.ExpectedVersionColumn}\" {expectedType};";
    }

    public override string CopyNewDocumentsFromTempTable()
    {
        // Temp table carries an extra mt_expected_version column when
        // versioning is active. INSERT INTO main (SELECT * FROM temp)
        // would pull that column into main and fail (more expressions
        // than target columns), so list columns explicitly in that case.
        if (_needsExpectedVersion)
        {
            var columnList = ColumnList(includeExpectedVersion: false);
            return
                $"insert into {_mapping.TableName.QualifiedName}({columnList}) (select {columnList} from {_tempTableName}) " +
                $"on conflict {ConflictKey()} do nothing";
        }

        return
            $"insert into {_mapping.TableName.QualifiedName} (select * from {_tempTableName}) " +
            $"on conflict {ConflictKey()} do nothing";
    }

    public override string OverwriteDuplicatesFromTempTable()
    {
        // SET data, plus every write binder's column — including
        // server-side ones (LastModified) since the COPY path carries
        // client-computed values for them through the temp table.
        var assignments = new List<string>(8) { "data = excluded.data" };
        foreach (var b in _descriptor.WriteBinders)
        {
            assignments.Add($"{b.ColumnName} = excluded.{b.ColumnName}");
        }

        // When the temp table carries an extra mt_expected_version column
        // (only on version-aware mappings), the INSERT SELECT must list
        // columns explicitly so the helper column doesn't leak into the
        // main table — INSERT INTO main(cols…) SELECT cols… avoids the
        // count mismatch. For non-versioned mappings the temp table has
        // exactly the same shape as main and SELECT * is fine.
        if (_needsExpectedVersion)
        {
            var columnList = ColumnList(includeExpectedVersion: false);
            return
                $"insert into {_mapping.TableName.QualifiedName}({columnList}) (select {columnList} from {_tempTableName}) " +
                $"on conflict {ConflictKey()} do update set {assignments.Join(", ")}";
        }

        return
            $"insert into {_mapping.TableName.QualifiedName} (select * from {_tempTableName}) " +
            $"on conflict {ConflictKey()} do update set {assignments.Join(", ")}";
    }

    public override string OverwriteDuplicatesFromTempTableWithVersionCheck()
    {
        if (!_needsExpectedVersion)
        {
            return OverwriteDuplicatesFromTempTable();
        }

        // UPDATE FROM ... WHERE target.mt_version = source.mt_expected_version.
        // Mirrors the codegen path: existing rows whose stored version
        // matches the caller-supplied expected version get overwritten;
        // mismatches are silently skipped and survive CopyNewDocumentsFromTempTable's
        // ON CONFLICT DO NOTHING. Net result: stale-version rows keep
        // their old values.
        var storageTable = _mapping.TableName.QualifiedName;
        var versionColumn = Marten.Schema.SchemaConstants.VersionColumn;
        var expectedColumn = Marten.Schema.SchemaConstants.ExpectedVersionColumn;

        var setColumns = new List<string>(8) { "data = source.data" };
        foreach (var b in _descriptor.WriteBinders)
        {
            setColumns.Add($"{b.ColumnName} = source.{b.ColumnName}");
        }

        var join = _descriptor.IsConjoined
            ? $"source.id = target.id and source.{Marten.Storage.Metadata.TenantIdColumn.Name} = target.{Marten.Storage.Metadata.TenantIdColumn.Name}"
            : "source.id = target.id";

        return
            $"update {storageTable} target set {setColumns.Join(", ")} " +
            $"from {_tempTableName} source " +
            $"where {join} and target.{versionColumn} = source.{expectedColumn}";
    }

    public override async Task LoadTempRowAsync(NpgsqlBinaryImporter writer, TDoc document, Tenant tenant,
        ISerializer serializer, CancellationToken cancellation)
    {
        // Snapshot the caller-supplied expected version BEFORE LoadRowAsync
        // — the version binder mutates document.Version to a fresh value
        // during the main column writes, so reading it afterwards would
        // pick up the new version rather than the expected one.
        long? expectedRevision = _revisionGetter?.Invoke(document);
        Guid? expectedVersion = _versionGetter?.Invoke(document);

        await LoadRowAsync(writer, document, tenant, serializer, cancellation).ConfigureAwait(false);

        if (!_needsExpectedVersion) return;

        // mt_expected_version: the caller-supplied "version we last saw"
        // taken from the document's [Version] / [Revision] member. Empty
        // Guid / non-positive long means "no expectation" — write NULL
        // so the comparison in OverwriteDuplicatesFromTempTableWithVersionCheck
        // (target.mt_version = source.mt_expected_version) fails and the
        // row is treated as a new-insert candidate, not an overwrite.
        if (expectedRevision is { } revision)
        {
            if (revision <= 0L)
            {
                await writer.WriteNullAsync(cancellation).ConfigureAwait(false);
            }
            else
            {
                await writer.WriteAsync(revision, NpgsqlDbType.Bigint, cancellation).ConfigureAwait(false);
            }
        }
        else if (expectedVersion is { } version)
        {
            if (version == Guid.Empty)
            {
                await writer.WriteNullAsync(cancellation).ConfigureAwait(false);
            }
            else
            {
                await writer.WriteAsync(version, NpgsqlDbType.Uuid, cancellation).ConfigureAwait(false);
            }
        }
        else
        {
            // No mapped version member — caller can't supply an expected
            // version. NULL ensures the version-check WHERE clause fails
            // and the row falls through to the new-insert pass.
            await writer.WriteNullAsync(cancellation).ConfigureAwait(false);
        }
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

        // Each metadata binder contributes its column value. The fixed metadata binders return a
        // dialect-neutral BulkColumnValue; duplicated fields carry an arbitrary provider type and
        // stay on the Postgres-native write (#4828).
        foreach (var binder in _descriptor.WriteBinders)
        {
            if (binder is DocumentDuplicatedFieldBinder<TDoc> duplicated)
            {
                await duplicated.WriteToBulkAsync(writer, document, serializer, cancellation).ConfigureAwait(false);
            }
            else
            {
                await WriteBulkValueAsync(writer, binder.GetBulkValue(document), cancellation).ConfigureAwait(false);
            }
        }
    }

    private static Task WriteBulkValueAsync(NpgsqlBinaryImporter writer, BulkColumnValue column,
        CancellationToken cancellation)
    {
        if (column.Value is null)
        {
            return writer.WriteNullAsync(cancellation);
        }

        var dbType = column.Type switch
        {
            StorageColumnType.String => NpgsqlDbType.Varchar,
            StorageColumnType.Guid => NpgsqlDbType.Uuid,
            StorageColumnType.Long => NpgsqlDbType.Bigint,
            StorageColumnType.Int => NpgsqlDbType.Integer,
            StorageColumnType.Boolean => NpgsqlDbType.Boolean,
            StorageColumnType.Timestamp => NpgsqlDbType.TimestampTz,
            StorageColumnType.Json => NpgsqlDbType.Jsonb,
            _ => throw new ArgumentOutOfRangeException(nameof(column))
        };

        // DBNull → typed NULL of the column type (e.g. a JSONB null); otherwise the value itself.
        return column.Value is DBNull
            ? writer.WriteAsync<object>(DBNull.Value, dbType, cancellation)
            : writer.WriteAsync(column.Value, dbType, cancellation);
    }

    private string ColumnList(bool includeExpectedVersion)
    {
        var columns = new List<string>(4 + _descriptor.WriteBinders.Length);
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
        if (includeExpectedVersion)
        {
            columns.Add($"\"{Marten.Schema.SchemaConstants.ExpectedVersionColumn}\"");
        }
        return columns.Join(", ");
    }
}
