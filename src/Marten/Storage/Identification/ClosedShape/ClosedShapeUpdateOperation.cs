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
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Storage.Identification.ClosedShape;

/// <summary>
/// W3 spike (M3+M7): hand-written Update operation. Emits
/// <c>UPDATE … SET data = ?, … WHERE id = ? [and tenant_id = ?] [and mt_version = ?] RETURNING {id|mt_version}</c>.
/// Parameter ordering: data first, then each client-side binder, then
/// id, then tenant_id (conjoined), then expected version (optimistic).
/// </summary>
/// <remarks>
/// Postprocess branches on <see cref="ConcurrencyMode"/>: under
/// <c>Off</c> a missing row raises
/// <see cref="NonExistentDocumentException"/>; under
/// <c>Optimistic</c> a missing row OR a mismatch between the returned
/// and just-written version raises <see cref="ConcurrencyException"/>.
/// </remarks>
internal sealed class ClosedShapeUpdateOperation<TDoc, TId>: IDocumentStorageOperation
    where TDoc : notnull
    where TId : notnull
{
    private readonly TDoc _document;
    private readonly TId _id;
    private readonly string _tenantId;
    private readonly DocumentStorageDescriptor<TDoc, TId> _descriptor;
    private readonly Dictionary<TId, Guid>? _versions;
    private readonly Guid _newVersion;

    public ClosedShapeUpdateOperation(
        TDoc document,
        TId id,
        string tenantId,
        DocumentStorageDescriptor<TDoc, TId> descriptor,
        Dictionary<TId, Guid>? versions)
    {
        _document = document;
        _id = id;
        _tenantId = tenantId;
        _descriptor = descriptor;
        _versions = versions;
        if (descriptor.ConcurrencyMode == ConcurrencyMode.Optimistic)
        {
            _newVersion = CombGuidIdGeneration.NewGuid();
        }
    }

    public Type DocumentType => typeof(TDoc);

    public object Document => _document;

    public Marten.Internal.DirtyTracking.IChangeTracker ToTracker(IMartenSession session)
        => new Marten.Internal.DirtyTracking.ChangeTracker<TDoc>(session, _document);

    public OperationRole Role() => OperationRole.Update;

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        // Update SQL ordering:
        //   data (0), binders (1+), id (n), [tenant_id (n+1)], [expected version (last)]
        var parameters = builder.AppendWithParameters(_descriptor.UpdateSql, '?');

        session.Serializer.WriteToParameter(parameters[0], _document);

        var slot = 1;
        foreach (var binder in _descriptor.ClientSideWriteBinders)
        {
            if (_descriptor.ConcurrencyMode == ConcurrencyMode.Optimistic &&
                ReferenceEquals(binder, _descriptor.VersionBinder))
            {
                parameters[slot].Value = _newVersion;
                parameters[slot].NpgsqlDbType = NpgsqlDbType.Uuid;
                _descriptor.VersionBinder.ApplyVersionTo(_document, _newVersion);
            }
            else
            {
                binder.BindParameter(parameters[slot], _document, session);
            }
            slot++;
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
            // Expected version comes from session.Versions; an absent
            // entry binds DBNull, which makes the WHERE filter false
            // (every row's mt_version is non-null), so the update
            // affects zero rows → ConcurrencyException.
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
    }

    public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
    {
        if (!reader.Read())
        {
            exceptions.Add(MissingRowException());
            return;
        }

        if (_descriptor.ConcurrencyMode == ConcurrencyMode.Optimistic)
        {
            _versions![_id] = _newVersion;
        }
    }

    public async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            exceptions.Add(MissingRowException());
            return;
        }

        if (_descriptor.ConcurrencyMode == ConcurrencyMode.Optimistic)
        {
            _versions![_id] = _newVersion;
        }
    }

    private Exception MissingRowException()
        => _descriptor.ConcurrencyMode == ConcurrencyMode.Optimistic
            ? new ConcurrencyException(typeof(TDoc), _id)
            : new NonExistentDocumentException(typeof(TDoc), _id);
}
