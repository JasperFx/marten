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
/// <c>ConcurrencyMode.Numeric</c> closed-shape Overwrite. Same INSERT
/// VALUES revision CASE block as the Numeric Upsert path, but the SET
/// CASE is 2 slots (no WHERE guard). #4658 — the
/// <see cref="DocumentStorage{TDoc,TId}.OverwriteProjected"/> path passes
/// a null revisions tracker so the projection doesn't poison the
/// session's revision dictionary. #4659 leaf.
/// </summary>
internal sealed class NumericClosedShapeOverwriteOperation<TDoc, TId>: ClosedShapeOverwriteOperation<TDoc, TId>
    where TDoc : notnull
    where TId : notnull
{
    private readonly Dictionary<TId, long>? _revisions;

    public NumericClosedShapeOverwriteOperation(
        TDoc document,
        TId id,
        string tenantId,
        DocumentStorageDescriptor<TDoc, TId> descriptor,
        Dictionary<TId, long>? revisions)
        : base(document, id, tenantId, descriptor)
    {
        _revisions = revisions;
    }

    public override void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        var parameters = builder.AppendWithDbParameters(_descriptor.OverwriteSql, '?');
        var slot = BindPreOnConflictParameters(parameters, session);

        // DO UPDATE SET mt_version = CASE WHEN ? = 0 THEN current+1 ELSE ? END
        // No WHERE guard — Overwrite always wins.
        parameters[slot].Value = Revision;
        _descriptor.Dialect.SetParameterType(parameters[slot], StorageColumnType.Long);
        parameters[slot + 1].Value = Revision;
        _descriptor.Dialect.SetParameterType(parameters[slot + 1], StorageColumnType.Long);
    }

    public override async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        if (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var newRevision = await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);
            // #4658 — null tracker (OverwriteProjected) just skips the
            // tracker write. RevisionBinder write still happens so the
            // document's revision field is fresh.
            if (_revisions is not null)
            {
                _revisions[_id] = newRevision;
            }
            _descriptor.RevisionBinder!.ApplyRevisionTo(_document, newRevision);
        }
    }

    protected override int BindClientSideBinder(DbParameter[] parameters, int slot, IDocumentMetadataBinder<TDoc> binder, IStorageSession session)
    {
        if (ReferenceEquals(binder, _descriptor.RevisionBinder))
        {
            // INSERT VALUES CASE block — same shape as NumericClosedShapeUpsertOperation.
            var revisionColumnType = _descriptor.RevisionBinder!.RevisionColumnType;
            var revisionValue = revisionColumnType == StorageColumnType.Int
                ? (object)checked((int)Revision)
                : Revision;
            parameters[slot].Value = revisionValue;
            _descriptor.Dialect.SetParameterType(parameters[slot], revisionColumnType);
            slot++;

            if (_descriptor.UseVersionFromMatchingStream)
            {
                slot = BindUseVersionFromMatchingStreamSubquery(parameters, slot);
            }

            parameters[slot].Value = revisionValue;
            _descriptor.Dialect.SetParameterType(parameters[slot], revisionColumnType);
            return slot + 1;
        }

        binder.BindParameter(parameters[slot], _document, session);
        return slot + 1;
    }
}
