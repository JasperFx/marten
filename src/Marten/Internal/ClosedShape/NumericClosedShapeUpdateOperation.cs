#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using Marten.Exceptions;
using Npgsql;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// <c>ConcurrencyMode.Numeric</c> closed-shape Update. Two revision
/// slots in the SET CASE expression (<c>CASE WHEN ? = 0 THEN
/// current+1 ELSE ? END</c>) plus two in the trailing WHERE clause
/// (<c>? = 0 OR table.mt_version &lt; ?</c>). Missing RETURNING row →
/// revision-guard failure → <see cref="ConcurrencyException"/>. #4659 leaf.
/// </summary>
internal sealed class NumericClosedShapeUpdateOperation<TDoc, TId>: ClosedShapeUpdateOperation<TDoc, TId>
    where TDoc : notnull
    where TId : notnull
{
    private readonly Dictionary<TId, long>? _revisions;

    public NumericClosedShapeUpdateOperation(
        TDoc document,
        TId id,
        string tenantId,
        DocumentStorageDescriptor<TDoc, TId> descriptor,
        Dictionary<TId, long>? revisions)
        : base(document, id, tenantId, descriptor)
    {
        _revisions = revisions;
    }

    public override void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        var parameters = builder.AppendWithParameters(_descriptor.UpdateSql, '?');
        var slot = BindPreConcurrencyParameters(parameters, session);

        // Trailing WHERE (? = 0 or {table}.mt_version < ?) — bind the raw
        // Revision to both slots. #4614: parameter type tracks column width.
        var revisionDbType = _descriptor.RevisionBinder!.ColumnDbType;
        var revisionValue = revisionDbType == NpgsqlDbType.Integer
            ? (object)checked((int)Revision)
            : Revision;
        parameters[slot].Value = revisionValue;
        parameters[slot].NpgsqlDbType = revisionDbType;
        parameters[slot + 1].Value = revisionValue;
        parameters[slot + 1].NpgsqlDbType = revisionDbType;
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
        // #4667 — null tracker (the UpdateProjected path) skips the tracker
        // write. RevisionBinder still applies so the document's revision
        // field is fresh.
        if (_revisions is not null)
        {
            _revisions[_id] = newRevision;
        }
        _descriptor.RevisionBinder!.ApplyRevisionTo(_document, newRevision);
    }

    protected override int BindClientSideBinder(NpgsqlParameter[] parameters, int slot, IDocumentMetadataBinder<TDoc> binder, IMartenSession session)
    {
        if (ReferenceEquals(binder, _descriptor.RevisionBinder))
        {
            // SET mt_version = CASE WHEN ? = 0 THEN current+1 ELSE ? END
            // (2 slots — Update side never uses UseVersionFromMatchingStream)
            var revisionDbType = _descriptor.RevisionBinder!.ColumnDbType;
            var revisionValue = revisionDbType == NpgsqlDbType.Integer
                ? (object)checked((int)Revision)
                : Revision;
            parameters[slot].Value = revisionValue;
            parameters[slot].NpgsqlDbType = revisionDbType;
            parameters[slot + 1].Value = revisionValue;
            parameters[slot + 1].NpgsqlDbType = revisionDbType;
            return slot + 2;
        }

        binder.BindParameter(parameters[slot], _document, session);
        return slot + 1;
    }
}
