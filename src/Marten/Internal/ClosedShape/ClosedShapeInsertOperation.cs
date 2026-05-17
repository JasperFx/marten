#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Operations;
using Npgsql;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// W3 spike (M3+M7+M8): hand-written Insert operation. Emits
/// <c>INSERT … ON CONFLICT (id) DO NOTHING RETURNING {id|mt_version}</c>.
/// Parameter ordering matches <see cref="ClosedShapeUpsertOperation{TDoc, TId}"/>:
/// id, data, then each client-side metadata binder. RETURNING lets
/// <see cref="Postprocess"/> distinguish "row inserted" from "row already
/// existed" and raise <see cref="DocumentAlreadyExistsException"/> in the
/// latter case.
/// </summary>
/// <remarks>
/// Under <see cref="ConcurrencyMode.Optimistic"/> the operation generates
/// the new Guid version client-side at construction time; under
/// <see cref="ConcurrencyMode.Numeric"/> it binds the caller-supplied
/// (or default <c>0</c> = auto-increment to <c>1</c>) <c>Revision</c>.
/// Either way the new value is written back onto the document +
/// session.Versions in postprocess.
/// </remarks>
internal sealed class ClosedShapeInsertOperation<TDoc, TId>: IDocumentStorageOperation, IRevisionedOperation, JasperFx.Core.Exceptions.IExceptionTransform
    where TDoc : notnull
    where TId : notnull
{
    private readonly TDoc _document;
    private readonly TId _id;
    private readonly string _tenantId;
    private readonly DocumentStorageDescriptor<TDoc, TId> _descriptor;
    private readonly Dictionary<TId, Guid>? _versions;
    private readonly Dictionary<TId, long>? _revisions;
    private readonly Guid _newVersion;

    public ClosedShapeInsertOperation(
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

    public OperationRole Role() => OperationRole.Insert;

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        // Parameter ordering matches the descriptor's SQL:
        //   non-conjoined: id (0), data (1), client-side binders (2+)
        //   conjoined:     tenant_id (0), id (1), data (2), binders (3+)
        // Under Numeric mode, the revision binder consumes TWO ? slots
        // (the CASE WHEN ? = 0 THEN 1 ELSE ? END expression).
        var parameters = builder.AppendWithParameters(_descriptor.InsertSql, '?');

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

        // Project session-derived metadata (Correlation/Causation/
        // Headers/LastModifiedBy) onto the document BEFORE serialization
        // so the values flow into the JSON data column too. Mirrors the
        // codegen path's GenerateCodeToModifyDocument frames.
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
    }

    public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
    {
        if (!reader.Read())
        {
            exceptions.Add(new DocumentAlreadyExistsException(null, typeof(TDoc), _id));
            return;
        }

        ApplyConcurrencyResult(reader);
    }

    public async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            exceptions.Add(new DocumentAlreadyExistsException(null, typeof(TDoc), _id));
            return;
        }

        ApplyConcurrencyResult(reader);
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
            // CASE WHEN ? = 0 THEN 1 ELSE ? END — bind raw Revision to
            // both slots.
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
