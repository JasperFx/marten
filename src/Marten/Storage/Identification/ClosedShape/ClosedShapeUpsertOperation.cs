#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Internal.Operations;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Storage.Identification.ClosedShape;

/// <summary>
/// W3 spike (M1): hand-written upsert operation that consumes the
/// descriptor's pre-built SQL + client-side binder array. Mirrors what
/// the codegen path emits per document-mapping configuration; here a
/// single class drives any configuration via the descriptor.
/// </summary>
internal sealed class ClosedShapeUpsertOperation<TDoc, TId>: IDocumentStorageOperation
    where TDoc : notnull
    where TId : notnull
{
    private readonly TDoc _document;
    private readonly TId _id;
    private readonly DocumentStorageDescriptor<TDoc, TId> _descriptor;
    private readonly OperationRole _role;

    public ClosedShapeUpsertOperation(
        TDoc document,
        TId id,
        DocumentStorageDescriptor<TDoc, TId> descriptor,
        OperationRole role)
    {
        _document = document;
        _id = id;
        _descriptor = descriptor;
        _role = role;
    }

    public Type DocumentType => typeof(TDoc);

    public object Document => _document;

    public Marten.Internal.DirtyTracking.IChangeTracker ToTracker(IMartenSession session)
        => new Marten.Internal.DirtyTracking.ChangeTracker<TDoc>(session, _document);

    public OperationRole Role() => _role;

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        // Closed-shape ConfigureCommand: hand the descriptor's pre-built
        // SQL (with `?` placeholders for client-side params and inline
        // literals for server-side ones) to AppendWithParameters. The
        // returned array has one entry per `?`, in order: id, data, then
        // each client-side binder.
        var parameters = builder.AppendWithParameters(_descriptor.UpsertSql, '?');

        // id (slot 0)
        parameters[0].Value = _id;
        parameters[0].NpgsqlDbType = PostgresqlProvider.Instance.ToParameterType(typeof(TId));

        // data (slot 1) — serializer writes directly into the parameter
        // (UTF-8 byte array, no intermediate string).
        session.Serializer.WriteToParameter(parameters[1], _document);

        // Metadata binders fill the remaining slots in order.
        var slot = 2;
        foreach (var binder in _descriptor.ClientSideWriteBinders)
        {
            binder.BindParameter(parameters[slot], _document, session);
            slot++;
        }
    }

    public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
    {
        // No RETURNING clause on the spike's upsert SQL yet — concurrency
        // / revision variants (M3) add it.
    }

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
        => Task.CompletedTask;
}
