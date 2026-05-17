#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Operations;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Storage.Identification.ClosedShape;

/// <summary>
/// W3 spike (M3): hand-written Update operation. Emits
/// <c>UPDATE … SET data = ?, … WHERE id = ? RETURNING id</c>.
/// Parameter ordering differs from
/// <see cref="ClosedShapeInsertOperation{TDoc, TId}"/> /
/// <see cref="ClosedShapeUpsertOperation{TDoc, TId}"/>: data first, then
/// each client-side binder, then id (the WHERE clause). Postprocess
/// raises <see cref="NonExistentDocumentException"/> when no row comes
/// back — that's the "tried to update a row that doesn't exist" case.
/// </summary>
internal sealed class ClosedShapeUpdateOperation<TDoc, TId>: IDocumentStorageOperation
    where TDoc : notnull
    where TId : notnull
{
    private readonly TDoc _document;
    private readonly TId _id;
    private readonly string _tenantId;
    private readonly DocumentStorageDescriptor<TDoc, TId> _descriptor;

    public ClosedShapeUpdateOperation(
        TDoc document,
        TId id,
        string tenantId,
        DocumentStorageDescriptor<TDoc, TId> descriptor)
    {
        _document = document;
        _id = id;
        _tenantId = tenantId;
        _descriptor = descriptor;
    }

    public Type DocumentType => typeof(TDoc);

    public object Document => _document;

    public Marten.Internal.DirtyTracking.IChangeTracker ToTracker(IMartenSession session)
        => new Marten.Internal.DirtyTracking.ChangeTracker<TDoc>(session, _document);

    public OperationRole Role() => OperationRole.Update;

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        // Update SQL ordering:
        //   non-conjoined: data (0), binders (1+), id (last)
        //   conjoined:     data (0), binders (1+), id (n-1), tenant_id (n)
        var parameters = builder.AppendWithParameters(_descriptor.UpdateSql, '?');

        session.Serializer.WriteToParameter(parameters[0], _document);

        var slot = 1;
        foreach (var binder in _descriptor.ClientSideWriteBinders)
        {
            binder.BindParameter(parameters[slot], _document, session);
            slot++;
        }

        parameters[slot].Value = _id;
        parameters[slot].NpgsqlDbType = PostgresqlProvider.Instance.ToParameterType(typeof(TId));
        slot++;

        if (_descriptor.IsConjoined)
        {
            parameters[slot].Value = _tenantId;
            parameters[slot].NpgsqlDbType = NpgsqlDbType.Varchar;
        }
    }

    public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
    {
        if (!reader.Read())
        {
            exceptions.Add(new NonExistentDocumentException(typeof(TDoc), _id));
        }
    }

    public async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            exceptions.Add(new NonExistentDocumentException(typeof(TDoc), _id));
        }
    }
}
