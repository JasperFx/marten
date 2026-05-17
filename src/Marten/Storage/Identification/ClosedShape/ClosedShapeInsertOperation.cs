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
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Storage.Identification.ClosedShape;

/// <summary>
/// W3 spike (M3+M7): hand-written Insert operation. Emits
/// <c>INSERT … ON CONFLICT (id) DO NOTHING RETURNING {id|mt_version}</c>.
/// Parameter ordering matches <see cref="ClosedShapeUpsertOperation{TDoc, TId}"/>:
/// id, data, then each client-side metadata binder. RETURNING lets
/// <see cref="Postprocess"/> distinguish "row inserted" from "row already
/// existed" and raise <see cref="DocumentAlreadyExistsException"/> in the
/// latter case.
/// </summary>
/// <remarks>
/// Under <see cref="ConcurrencyMode.Optimistic"/> the operation generates
/// the new Guid version client-side at construction time, binds it via
/// the version binder, and writes the value back onto the document +
/// session.Versions in postprocess so subsequent updates can supply it
/// as the expected version.
/// </remarks>
internal sealed class ClosedShapeInsertOperation<TDoc, TId>: IDocumentStorageOperation
    where TDoc : notnull
    where TId : notnull
{
    private readonly TDoc _document;
    private readonly TId _id;
    private readonly string _tenantId;
    private readonly DocumentStorageDescriptor<TDoc, TId> _descriptor;
    private readonly Dictionary<TId, Guid>? _versions;
    private readonly Guid _newVersion;

    public ClosedShapeInsertOperation(
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

    public OperationRole Role() => OperationRole.Insert;

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        // Parameter ordering matches the descriptor's SQL:
        //   non-conjoined: id (0), data (1), client-side binders (2+)
        //   conjoined:     tenant_id (0), id (1), data (2), binders (3+)
        var parameters = builder.AppendWithParameters(_descriptor.InsertSql, '?');

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
                // Use the version generated at construction time so
                // postprocess can validate the row returned the same
                // value we wrote.
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
        // RETURNING — if no row came back, ON CONFLICT DO NOTHING fired
        // and the row already exists. Surface as
        // DocumentAlreadyExistsException matching the codegen path's
        // behavior.
        if (!reader.Read())
        {
            exceptions.Add(new DocumentAlreadyExistsException(null, typeof(TDoc), _id));
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
            exceptions.Add(new DocumentAlreadyExistsException(null, typeof(TDoc), _id));
            return;
        }

        if (_descriptor.ConcurrencyMode == ConcurrencyMode.Optimistic)
        {
            _versions![_id] = _newVersion;
        }
    }
}
