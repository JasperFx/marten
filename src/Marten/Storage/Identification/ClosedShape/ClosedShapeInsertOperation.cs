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
/// W3 spike (M3): hand-written Insert operation. Emits
/// <c>INSERT … ON CONFLICT (id) DO NOTHING RETURNING id</c>. Parameter
/// ordering matches <see cref="ClosedShapeUpsertOperation{TDoc, TId}"/>:
/// id, data, then each client-side metadata binder. RETURNING lets
/// <see cref="Postprocess"/> distinguish "row inserted" from "row already
/// existed" and raise <see cref="DocumentAlreadyExistsException"/> in the
/// latter case.
/// </summary>
internal sealed class ClosedShapeInsertOperation<TDoc, TId>: IDocumentStorageOperation
    where TDoc : notnull
    where TId : notnull
{
    private readonly TDoc _document;
    private readonly TId _id;
    private readonly DocumentStorageDescriptor<TDoc, TId> _descriptor;

    public ClosedShapeInsertOperation(
        TDoc document,
        TId id,
        DocumentStorageDescriptor<TDoc, TId> descriptor)
    {
        _document = document;
        _id = id;
        _descriptor = descriptor;
    }

    public Type DocumentType => typeof(TDoc);

    public object Document => _document;

    public Marten.Internal.DirtyTracking.IChangeTracker ToTracker(IMartenSession session)
        => new Marten.Internal.DirtyTracking.ChangeTracker<TDoc>(session, _document);

    public OperationRole Role() => OperationRole.Insert;

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        // Same parameter ordering as upsert: id at slot 0, data at slot 1,
        // then client-side binders. ON CONFLICT DO NOTHING + RETURNING
        // are pre-baked into _descriptor.InsertSql.
        var parameters = builder.AppendWithParameters(_descriptor.InsertSql, '?');

        parameters[0].Value = _id;
        parameters[0].NpgsqlDbType = PostgresqlProvider.Instance.ToParameterType(typeof(TId));

        session.Serializer.WriteToParameter(parameters[1], _document);

        var slot = 2;
        foreach (var binder in _descriptor.ClientSideWriteBinders)
        {
            binder.BindParameter(parameters[slot], _document, session);
            slot++;
        }
    }

    public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
    {
        // RETURNING id — if no row, ON CONFLICT DO NOTHING fired and the
        // row already exists. Surface as DocumentAlreadyExistsException
        // matching the codegen path's behavior.
        if (!reader.Read())
        {
            exceptions.Add(new DocumentAlreadyExistsException(null, typeof(TDoc), _id));
        }
    }

    public async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            exceptions.Add(new DocumentAlreadyExistsException(null, typeof(TDoc), _id));
        }
    }
}
