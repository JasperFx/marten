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
/// <c>ConcurrencyMode.Numeric</c> closed-shape Insert. Binds the revision
/// <c>CASE WHEN ? = 0 THEN … ELSE ? END</c> block (2–4 parameter slots
/// depending on <see cref="DocumentStorageDescriptor{TDoc,TId}.UseVersionFromMatchingStream"/>
/// + <see cref="DocumentStorageDescriptor{TDoc,TId}.IsConjoined"/>),
/// reads the resolved revision out of the RETURNING row, and writes it
/// back through <see cref="DocumentRevisionBinder{TDoc}.ApplyRevisionTo"/>
/// + the session's per-type revision tracker. #4659 leaf.
/// </summary>
internal sealed class NumericClosedShapeInsertOperation<TDoc, TId>: ClosedShapeInsertOperation<TDoc, TId>
    where TDoc : notnull
    where TId : notnull
{
    private readonly Dictionary<TId, long>? _revisions;

    public NumericClosedShapeInsertOperation(
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
        var parameters = builder.AppendWithParameters(_descriptor.InsertSql, '?');
        var slot = BindLeadingParameters(parameters, session);

        foreach (var binder in _descriptor.ClientSideWriteBinders)
        {
            if (ReferenceEquals(binder, _descriptor.RevisionBinder))
            {
                slot = BindRevisionBlock(parameters, slot);
            }
            else
            {
                binder.BindParameter(parameters[slot], _document, session);
                slot++;
            }
        }
    }

    public override async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            exceptions.Add(new DocumentAlreadyExistsException(null, typeof(TDoc), _id));
            return;
        }

        var newRevision = await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);
        // #4667 — null tracker (the InsertProjected path) skips the tracker
        // write. RevisionBinder still applies so the document's revision
        // field is fresh.
        if (_revisions is not null)
        {
            _revisions[_id] = newRevision;
        }
        _descriptor.RevisionBinder!.ApplyRevisionTo(_document, newRevision);
    }

    /// <summary>
    /// Numeric Insert binds the revision CASE expression in two/three/four
    /// slots:
    /// <list type="bullet">
    /// <item>Default: <c>CASE WHEN ? = 0 THEN 1 ELSE ? END</c> (2 slots).</item>
    /// <item><c>UseVersionFromMatchingStream</c> (non-conjoined):
    ///   <c>CASE WHEN ? = 0 THEN COALESCE((select version from mt_streams where id = ?), 1) ELSE ? END</c>
    ///   (3 slots — the <c>? = 0</c> check, the id subquery, the explicit revision).</item>
    /// <item><c>UseVersionFromMatchingStream + IsConjoined</c>: extra <c>?</c> for
    ///   <c>tenant_id</c> inside the subquery (4 slots).</item>
    /// </list>
    /// #4614: the parameter type tracks the column width (integer vs bigint)
    /// so the CASE branch types align.
    /// </summary>
    private int BindRevisionBlock(NpgsqlParameter[] parameters, int slot)
    {
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
}
