#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Internal;
using Marten.Internal.Operations;
using Npgsql;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// W3 spike (M7+M8): hand-written Overwrite operation. Same shape as
/// <see cref="ClosedShapeUpsertOperation{TDoc, TId}"/> except the
/// trailing concurrency guard is dropped — the caller has explicitly
/// asked to bypass the optimistic / numeric check
/// (<c>session.Store(doc, ignoreConcurrencyCheck: true)</c> or
/// <see cref="Marten.Services.ConcurrencyChecks.Disabled"/> session
/// option). Under <see cref="ConcurrencyMode.Off"/> overwrite is
/// functionally identical to upsert.
/// </summary>
internal sealed class ClosedShapeOverwriteOperation<TDoc, TId>: IDocumentStorageOperation, IRevisionedOperation, IIdentifiedOperation<TDoc, TId>, JasperFx.Core.Exceptions.IExceptionTransform
    where TDoc : notnull
    where TId : notnull
{
    public TId Id => _id;

    private readonly TDoc _document;
    private readonly TId _id;
    private readonly string _tenantId;
    private readonly DocumentStorageDescriptor<TDoc, TId> _descriptor;
    private readonly Dictionary<TId, Guid>? _versions;
    private readonly Dictionary<TId, long>? _revisions;
    private readonly Guid _newVersion;

    public ClosedShapeOverwriteOperation(
        TDoc document,
        TId id,
        string tenantId,
        DocumentStorageDescriptor<TDoc, TId> descriptor,
        Dictionary<TId, Guid>? versions,
        Dictionary<TId, long>? revisions)
    {
        _document = document;
        _id = id;
        _tenantId = tenantId;
        _descriptor = descriptor;
        _versions = versions;
        _revisions = revisions;
        if (descriptor.ConcurrencyMode == ConcurrencyMode.Optimistic)
        {
            _newVersion = CombGuidIdGeneration.NewGuid();
        }
    }

    public long Revision { get; set; }

    public bool IgnoreConcurrencyViolation { get; set; }

    public Type DocumentType => typeof(TDoc);

    public object Document => _document;

    public Marten.Internal.DirtyTracking.IChangeTracker ToTracker(IMartenSession session)
        => new Marten.Internal.DirtyTracking.ChangeTracker<TDoc>(session, _document);

    public OperationRole Role() => OperationRole.Update;

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        var parameters = builder.AppendWithParameters(_descriptor.OverwriteSql, '?');

        var slot = 0;
        if (_descriptor.IsConjoined)
        {
            parameters[slot].Value = _tenantId;
            parameters[slot].NpgsqlDbType = NpgsqlDbType.Varchar;
            slot++;
        }

        parameters[slot].Value = _descriptor.Identification.ToRawSqlValue(_id);
        parameters[slot].NpgsqlDbType = PostgresqlProvider.Instance.ToParameterType(_descriptor.Identification.RawSqlType);
        slot++;

        // Project session-derived metadata onto the document BEFORE
        // serialization so the values flow into the JSON data column.
        foreach (var binder in _descriptor.WriteBinders)
        {
            binder.ApplyToDocument(_document, session);
        }

        session.Serializer.WriteToParameter(parameters[slot], _document);
        slot++;

        foreach (var binder in _descriptor.ClientSideWriteBinders)
        {
            slot = BindBinder(parameters, slot, binder, session);
        }

        if (_descriptor.ConcurrencyMode == ConcurrencyMode.Numeric)
        {
            // DO UPDATE SET mt_version = CASE WHEN ? = 0 THEN current+1 ELSE ? END
            // No WHERE guard — Overwrite always wins.
            parameters[slot].Value = Revision;
            parameters[slot].NpgsqlDbType = NpgsqlDbType.Bigint;
            parameters[slot + 1].Value = Revision;
            parameters[slot + 1].NpgsqlDbType = NpgsqlDbType.Bigint;
        }
    }

    public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
    {
        if (_descriptor.ConcurrencyMode == ConcurrencyMode.Off) return;

        if (reader.Read())
        {
            ApplyConcurrencyResult(reader);
        }
    }

    public async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        if (_descriptor.ConcurrencyMode == ConcurrencyMode.Off) return;

        if (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            ApplyConcurrencyResult(reader);
        }
    }

    private int BindBinder(NpgsqlParameter[] parameters, int slot, IDocumentMetadataBinder<TDoc> binder, IMartenSession session)
    {
        if (_descriptor.ConcurrencyMode == ConcurrencyMode.Optimistic &&
            ReferenceEquals(binder, _descriptor.VersionBinder))
        {
            parameters[slot].Value = _newVersion;
            parameters[slot].NpgsqlDbType = NpgsqlDbType.Uuid;
            _descriptor.VersionBinder.ApplyVersionTo(_document, _newVersion);
            return slot + 1;
        }

        if (_descriptor.ConcurrencyMode == ConcurrencyMode.Numeric &&
            ReferenceEquals(binder, _descriptor.RevisionBinder))
        {
            parameters[slot].Value = Revision;
            parameters[slot].NpgsqlDbType = NpgsqlDbType.Bigint;
            parameters[slot + 1].Value = Revision;
            parameters[slot + 1].NpgsqlDbType = NpgsqlDbType.Bigint;
            return slot + 2;
        }

        binder.BindParameter(parameters[slot], _document, session);
        return slot + 1;
    }

    private void ApplyConcurrencyResult(DbDataReader reader)
    {
        switch (_descriptor.ConcurrencyMode)
        {
            case ConcurrencyMode.Optimistic:
                _versions![_id] = _newVersion;
                break;
            case ConcurrencyMode.Numeric:
                var newRevision = reader.GetFieldValue<long>(0);
                _revisions![_id] = newRevision;
                _descriptor.RevisionBinder?.ApplyRevisionTo(_document, newRevision);
                break;
        }
    }
    public bool TryTransform(System.Exception original, out System.Exception? transformed)
        => ClosedShapeOperationExceptionTransform.TryTransform(original, _descriptor.TableName, typeof(TDoc), _id!, out transformed);

}
