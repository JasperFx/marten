#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Internal;
using Marten.Internal.Operations;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Storage.Identification.ClosedShape;

/// <summary>
/// W3 spike (M7): hand-written Overwrite operation. Same shape as
/// <see cref="ClosedShapeUpsertOperation{TDoc, TId}"/> except the
/// trailing <c>where mt_version = ?</c> filter is dropped — the
/// caller has explicitly asked to bypass the optimistic-concurrency
/// check (today's <c>session.Store(doc, ignoreConcurrencyCheck: true)</c>).
/// When <see cref="ConcurrencyMode"/> is <c>Off</c>, overwrite is
/// functionally identical to upsert.
/// </summary>
internal sealed class ClosedShapeOverwriteOperation<TDoc, TId>: IDocumentStorageOperation
    where TDoc : notnull
    where TId : notnull
{
    private readonly TDoc _document;
    private readonly TId _id;
    private readonly string _tenantId;
    private readonly DocumentStorageDescriptor<TDoc, TId> _descriptor;
    private readonly Dictionary<TId, Guid>? _versions;
    private readonly Guid _newVersion;

    public ClosedShapeOverwriteOperation(
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
        var parameters = builder.AppendWithParameters(_descriptor.OverwriteSql, '?');

        var slot = 0;
        if (_descriptor.IsConjoined)
        {
            parameters[slot].Value = _tenantId;
            parameters[slot].NpgsqlDbType = NpgsqlDbType.Varchar;
            slot++;
        }

        parameters[slot].Value = _id;
        parameters[slot].NpgsqlDbType = PostgresqlProvider.Instance.ToParameterType(typeof(TId));
        slot++;

        session.Serializer.WriteToParameter(parameters[slot], _document);
        slot++;

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
    }

    public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
    {
        if (_descriptor.ConcurrencyMode == ConcurrencyMode.Off)
        {
            return;
        }

        // No version WHERE filter → the write always happens, so a
        // returned row is guaranteed. Capture the new version for
        // subsequent updates.
        if (reader.Read())
        {
            _versions![_id] = _newVersion;
        }
    }

    public async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        if (_descriptor.ConcurrencyMode == ConcurrencyMode.Off)
        {
            return;
        }

        if (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            _versions![_id] = _newVersion;
        }
    }
}
