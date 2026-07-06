#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Weasel.Core;
using Weasel.Postgresql;

using Marten.Internal.Storage;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// <c>ConcurrencyMode.Off</c> closed-shape Overwrite. Functionally
/// identical to <see cref="UnversionedClosedShapeUpsertOperation{TDoc, TId}"/>
/// — no concurrency guard either way under Off. PostprocessAsync is
/// fire-and-forget. #4659 leaf.
/// </summary>
internal sealed class UnversionedClosedShapeOverwriteOperation<TDoc, TId>: ClosedShapeOverwriteOperation<TDoc, TId>
    where TDoc : notnull
    where TId : notnull
{
    public UnversionedClosedShapeOverwriteOperation(
        TDoc document,
        TId id,
        string tenantId,
        DocumentStorageDescriptor<TDoc, TId> descriptor)
        : base(document, id, tenantId, descriptor)
    {
    }

    public override void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        var parameters = builder.AppendWithDbParameters(_descriptor.OverwriteSql, '?');
        BindPreOnConflictParameters(parameters, session);
        // Off-mode OverwriteSql has no trailing concurrency-extras slot.
    }

    public override Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
        => Task.CompletedTask;

    protected override int BindClientSideBinder(DbParameter[] parameters, int slot, IDocumentMetadataBinder<TDoc> binder, IStorageSession session)
    {
        binder.BindParameter(parameters[slot], _document, session);
        return slot + 1;
    }
}
