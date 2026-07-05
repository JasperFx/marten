#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Exceptions;
using Npgsql;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// <c>ConcurrencyMode.Off</c> closed-shape Update. Plain
/// <c>WHERE id = ? [AND tenant_id = ?] [AND partition_col = ?…]</c> —
/// no concurrency guard. A missing row signals the document didn't
/// exist; raises <see cref="NonExistentDocumentException"/>. #4659 leaf.
/// </summary>
internal sealed class UnversionedClosedShapeUpdateOperation<TDoc, TId>: ClosedShapeUpdateOperation<TDoc, TId>
    where TDoc : notnull
    where TId : notnull
{
    public UnversionedClosedShapeUpdateOperation(
        TDoc document,
        TId id,
        string tenantId,
        DocumentStorageDescriptor<TDoc, TId> descriptor)
        : base(document, id, tenantId, descriptor)
    {
    }

    public override void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        var parameters = builder.AppendWithParameters(_descriptor.UpdateSql, '?');
        // Off mode has no trailing concurrency-guard slot — we just consume
        // data + binders + id + tenant + partition PK.
        BindPreConcurrencyParameters(parameters, session);
    }

    public override async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            if (!IgnoreConcurrencyViolation)
            {
                exceptions.Add(new NonExistentDocumentException(typeof(TDoc), _id));
            }
        }
    }

    protected override int BindClientSideBinder(NpgsqlParameter[] parameters, int slot, IDocumentMetadataBinder<TDoc> binder, IStorageSession session)
    {
        binder.BindParameter(parameters[slot], _document, session);
        return slot + 1;
    }
}
