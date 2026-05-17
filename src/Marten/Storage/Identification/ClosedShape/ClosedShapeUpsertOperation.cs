#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Operations;
using Npgsql;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Storage.Identification.ClosedShape;

/// <summary>
/// W3 spike (M1+M7+M8): hand-written upsert operation that consumes the
/// descriptor's pre-built SQL + client-side binder array.
/// </summary>
/// <remarks>
/// Concurrency variants:
/// <list type="bullet">
/// <item><c>Optimistic</c>: ON CONFLICT DO UPDATE adds <c>where mt_version = ?</c>
/// (caller-supplied expected version). A mismatch produces no
/// RETURNING row → <see cref="ConcurrencyException"/>.</item>
/// <item><c>Numeric</c>: revision binder writes
/// <c>CASE WHEN ? = 0 THEN current+1 ELSE ? END</c>; ON CONFLICT
/// guard is <c>? = 0 OR current &lt; supplied</c>. A failed guard
/// surfaces as <see cref="ConcurrencyException"/> unless
/// <see cref="IRevisionedOperation.IgnoreConcurrencyViolation"/> is
/// set.</item>
/// </list>
/// </remarks>
internal sealed class ClosedShapeUpsertOperation<TDoc, TId>: IDocumentStorageOperation, IRevisionedOperation
    where TDoc : notnull
    where TId : notnull
{
    private readonly TDoc _document;
    private readonly TId _id;
    private readonly string _tenantId;
    private readonly DocumentStorageDescriptor<TDoc, TId> _descriptor;
    private readonly OperationRole _role;
    private readonly Dictionary<TId, Guid>? _versions;
    private readonly Dictionary<TId, long>? _revisions;
    private readonly Guid _newVersion;

    public ClosedShapeUpsertOperation(
        TDoc document,
        TId id,
        string tenantId,
        DocumentStorageDescriptor<TDoc, TId> descriptor,
        OperationRole role,
        Dictionary<TId, Guid>? versions,
        Dictionary<TId, long>? revisions)
    {
        _document = document;
        _id = id;
        _tenantId = tenantId;
        _descriptor = descriptor;
        _role = role;
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

    public OperationRole Role() => _role;

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        // Upsert SQL ordering (non-conjoined, Numeric):
        //   id, data, rev (INSERT CASE × 2), [other binders],
        //   rev (SET CASE × 2), rev (WHERE × 2)
        var parameters = builder.AppendWithParameters(_descriptor.UpsertSql, '?');

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

        session.Serializer.WriteToParameter(parameters[slot], _document);
        slot++;

        foreach (var binder in _descriptor.ClientSideWriteBinders)
        {
            slot = BindBinder(parameters, slot, binder, session);
        }

        // ON CONFLICT side concurrency-related extras.
        if (_descriptor.ConcurrencyMode == ConcurrencyMode.Optimistic)
        {
            // ON CONFLICT DO UPDATE ... WHERE table.mt_version = ?
            if (_versions!.TryGetValue(_id, out var expected))
            {
                parameters[slot].Value = expected;
            }
            else
            {
                parameters[slot].Value = DBNull.Value;
            }
            parameters[slot].NpgsqlDbType = NpgsqlDbType.Uuid;
        }
        else if (_descriptor.ConcurrencyMode == ConcurrencyMode.Numeric)
        {
            // DO UPDATE SET mt_version = CASE WHEN ? = 0 THEN current+1 ELSE ? END
            // WHERE ? = 0 OR table.mt_version < ?
            for (var i = 0; i < 4; i++)
            {
                parameters[slot + i].Value = Revision;
                parameters[slot + i].NpgsqlDbType = NpgsqlDbType.Bigint;
            }
        }
    }

    public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
    {
        if (_descriptor.ConcurrencyMode == ConcurrencyMode.Off)
        {
            // Mode Off Upsert is fire-and-forget today — RETURNING id is
            // there for symmetry with Insert/Update but the result isn't
            // inspected.
            return;
        }

        if (!reader.Read())
        {
            if (!IgnoreConcurrencyViolation)
            {
                exceptions.Add(new ConcurrencyException(typeof(TDoc), _id));
            }
            return;
        }

        ApplyConcurrencyResult(reader);
    }

    public async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        if (_descriptor.ConcurrencyMode == ConcurrencyMode.Off)
        {
            return;
        }

        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            if (!IgnoreConcurrencyViolation)
            {
                exceptions.Add(new ConcurrencyException(typeof(TDoc), _id));
            }
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
            // INSERT VALUES (..., CASE WHEN ? = 0 THEN 1 ELSE ? END, ...)
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
}
