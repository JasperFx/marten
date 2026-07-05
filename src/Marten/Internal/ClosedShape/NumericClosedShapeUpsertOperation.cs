#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using Marten.Exceptions;
using Marten.Internal.Operations;
using Npgsql;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// <c>ConcurrencyMode.Numeric</c> closed-shape Upsert. Two/three/four
/// revision slots in the INSERT VALUES CASE block (depending on
/// <see cref="DocumentStorageDescriptor{TDoc,TId}.UseVersionFromMatchingStream"/>
/// + <see cref="DocumentStorageDescriptor{TDoc,TId}.IsConjoined"/>) plus
/// four more in the ON CONFLICT DO UPDATE SET/WHERE block. Missing
/// RETURNING row → revision-guard failure →
/// <see cref="ConcurrencyException"/>. #4659 leaf.
/// </summary>
internal sealed class NumericClosedShapeUpsertOperation<TDoc, TId>: ClosedShapeUpsertOperation<TDoc, TId>
    where TDoc : notnull
    where TId : notnull
{
    private readonly Dictionary<TId, long>? _revisions;

    public NumericClosedShapeUpsertOperation(
        TDoc document,
        TId id,
        string tenantId,
        DocumentStorageDescriptor<TDoc, TId> descriptor,
        OperationRole role,
        Dictionary<TId, long>? revisions)
        : base(document, id, tenantId, descriptor, role)
    {
        _revisions = revisions;
    }

    public override void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        var parameters = builder.AppendWithParameters(_descriptor.UpsertSql, '?');
        var slot = BindPreOnConflictParameters(parameters, session);

        // ON CONFLICT trailing:
        //   DO UPDATE SET mt_version = CASE WHEN ? = 0 THEN current+1 ELSE ? END
        //   WHERE ? = 0 OR table.mt_version < ?
        for (var i = 0; i < 4; i++)
        {
            parameters[slot + i].Value = Revision;
            parameters[slot + i].NpgsqlDbType = NpgsqlDbType.Bigint;
        }
    }

    public override async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            if (!IgnoreConcurrencyViolation)
            {
                exceptions.Add(new ConcurrencyException(typeof(TDoc), _id));
            }
            return;
        }

        var newRevision = await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);
        // #4667 — null tracker (the UpsertProjected path) skips the tracker
        // write. RevisionBinder still applies so the document's revision
        // field is fresh.
        if (_revisions is not null)
        {
            _revisions[_id] = newRevision;
        }
        _descriptor.RevisionBinder!.ApplyRevisionTo(_document, newRevision);
    }

    protected override int BindClientSideBinder(NpgsqlParameter[] parameters, int slot, IDocumentMetadataBinder<TDoc> binder, IStorageSession session)
    {
        if (ReferenceEquals(binder, _descriptor.RevisionBinder))
        {
            // INSERT VALUES CASE block — see NumericClosedShapeInsertOperation
            // for the slot-count rationale.
            var revisionDbType = _descriptor.RevisionBinder!.ColumnDbType;
            var revisionValue = revisionDbType == NpgsqlDbType.Integer
                ? (object)checked((int)Revision)
                : Revision;
            parameters[slot].Value = revisionValue;
            parameters[slot].NpgsqlDbType = revisionDbType;
            slot++;

            if (_descriptor.UseVersionFromMatchingStream)
            {
                slot = BindUseVersionFromMatchingStreamSubquery(parameters, slot);
            }

            parameters[slot].Value = revisionValue;
            parameters[slot].NpgsqlDbType = revisionDbType;
            return slot + 1;
        }

        binder.BindParameter(parameters[slot], _document, session);
        return slot + 1;
    }
}
