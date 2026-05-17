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
/// W3 spike (M3+M7+M8): hand-written Update operation. Emits
/// <c>UPDATE … SET data = ?, … WHERE id = ? [and tenant_id = ?] [and concurrency-guard] RETURNING {id|mt_version}</c>.
/// </summary>
/// <remarks>
/// Postprocess branches on <see cref="ConcurrencyMode"/>:
/// <list type="bullet">
/// <item><c>Off</c>: a missing row raises <see cref="NonExistentDocumentException"/>.</item>
/// <item><c>Optimistic</c>: a missing row raises <see cref="ConcurrencyException"/>.</item>
/// <item><c>Numeric</c>: a missing row raises <see cref="ConcurrencyException"/> unless
/// <see cref="IRevisionedOperation.IgnoreConcurrencyViolation"/> is set.</item>
/// </list>
/// </remarks>
internal sealed class ClosedShapeUpdateOperation<TDoc, TId>: IDocumentStorageOperation, IRevisionedOperation
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

    public ClosedShapeUpdateOperation(
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
        // Update SQL ordering:
        //   data (0), binders (1+), id (n), [tenant_id], [guard params]
        // Numeric: revision binder consumes 2 slots in SET; WHERE adds 2
        // more slots for the same Revision value.
        var parameters = builder.AppendWithParameters(_descriptor.UpdateSql, '?');

        session.Serializer.WriteToParameter(parameters[0], _document);

        var slot = 1;
        foreach (var binder in _descriptor.ClientSideWriteBinders)
        {
            slot = BindBinder(parameters, slot, binder, session);
        }

        parameters[slot].Value = _id;
        parameters[slot].NpgsqlDbType = PostgresqlProvider.Instance.ToParameterType(typeof(TId));
        slot++;

        if (_descriptor.IsConjoined)
        {
            parameters[slot].Value = _tenantId;
            parameters[slot].NpgsqlDbType = NpgsqlDbType.Varchar;
            slot++;
        }

        if (_descriptor.ConcurrencyMode == ConcurrencyMode.Optimistic)
        {
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
            // WHERE (? = 0 or {table}.mt_version < ?) — bind raw
            // Revision to both slots.
            parameters[slot].Value = Revision;
            parameters[slot].NpgsqlDbType = NpgsqlDbType.Bigint;
            parameters[slot + 1].Value = Revision;
            parameters[slot + 1].NpgsqlDbType = NpgsqlDbType.Bigint;
        }
    }

    public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
    {
        if (!reader.Read())
        {
            if (!IgnoreConcurrencyViolation)
            {
                exceptions.Add(MissingRowException());
            }
            return;
        }

        ApplyConcurrencyResult(reader);
    }

    public async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            if (!IgnoreConcurrencyViolation)
            {
                exceptions.Add(MissingRowException());
            }
            return;
        }

        ApplyConcurrencyResult(reader);
    }

    private Exception MissingRowException()
        => _descriptor.ConcurrencyMode == ConcurrencyMode.Off
            ? new NonExistentDocumentException(typeof(TDoc), _id)
            : new ConcurrencyException(typeof(TDoc), _id);

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
            // SET mt_version = CASE WHEN ? = 0 THEN current+1 ELSE ? END
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
