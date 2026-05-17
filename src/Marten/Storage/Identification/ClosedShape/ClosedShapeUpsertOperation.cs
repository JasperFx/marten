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
    private readonly string _tenantId;
    private readonly DocumentStorageDescriptor<TDoc, TId> _descriptor;
    private readonly OperationRole _role;

    public ClosedShapeUpsertOperation(
        TDoc document,
        TId id,
        string tenantId,
        DocumentStorageDescriptor<TDoc, TId> descriptor,
        OperationRole role)
    {
        _document = document;
        _id = id;
        _tenantId = tenantId;
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
        // SQL to AppendWithParameters. Parameter ordering:
        //   non-conjoined: id, data, client-side binders
        //   conjoined:     tenant_id, id, data, client-side binders
        var parameters = builder.AppendWithParameters(_descriptor.UpsertSql, '?');

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
