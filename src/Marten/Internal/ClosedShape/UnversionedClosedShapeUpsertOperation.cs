#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal.Operations;
using Npgsql;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// <c>ConcurrencyMode.Off</c> closed-shape Upsert. Plain
/// <c>INSERT … ON CONFLICT (id) DO UPDATE SET … RETURNING id</c> — no
/// version/revision guard. PostprocessAsync is fire-and-forget: the
/// RETURNING row is there for symmetry with Insert/Update but the result
/// isn't inspected (Off-mode Upsert always wins, no concurrency-failure
/// outcome to detect). #4659 leaf.
/// </summary>
internal sealed class UnversionedClosedShapeUpsertOperation<TDoc, TId>: ClosedShapeUpsertOperation<TDoc, TId>
    where TDoc : notnull
    where TId : notnull
{
    public UnversionedClosedShapeUpsertOperation(
        TDoc document,
        TId id,
        string tenantId,
        DocumentStorageDescriptor<TDoc, TId> descriptor,
        OperationRole role)
        : base(document, id, tenantId, descriptor, role)
    {
    }

    public override void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        var parameters = builder.AppendWithParameters(_descriptor.UpsertSql, '?');
        BindPreOnConflictParameters(parameters, session);
        // Off-mode UpsertSql has no trailing concurrency-extras slot.
    }

    public override Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
        => Task.CompletedTask;

    protected override int BindClientSideBinder(NpgsqlParameter[] parameters, int slot, IDocumentMetadataBinder<TDoc> binder, IStorageSession session)
    {
        binder.BindParameter(parameters[slot], _document, session);
        return slot + 1;
    }
}
