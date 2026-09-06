#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Storage;
using Weasel.Postgresql;
using Weasel.Storage;

namespace Marten.Events.Operations;

/// <summary>
/// Claims a natural key for one stream, refusing the claim when the key is already mapped to a
/// different <em>live</em> stream (#5344, ruling in jasperfx#764/#772).
/// </summary>
/// <remarks>
/// <para>
/// Replaces the plain <c>ON CONFLICT ... DO UPDATE SET stream_id = excluded.stream_id</c> that the
/// append path used to queue through <c>QueueSqlCommand</c>. That form repointed the row at the
/// newcomer and reported nothing, which left the original stream in place but unreachable by the
/// identifier it was created with. Of the two failure modes the silent one is worse, so a natural
/// key already mapped to a live stream is now refused rather than moved.
/// </para>
/// <para>
/// <b>The refusal is the SQL, not a pre-flight read.</b> A probing SELECT before the write would
/// race: two sessions could both find the key free and both proceed, and the loser's upsert would
/// repoint the row exactly as before. Instead the <c>DO UPDATE</c> keeps firing unconditionally but
/// its <c>SET</c> is a <c>CASE</c> that writes the incoming stream only when the claim is legitimate
/// and otherwise rewrites the row's existing value — so the row is never repointed, and the
/// row-level lock ON CONFLICT already takes is what serializes concurrent claimants. Because the
/// update always fires, <c>RETURNING</c> always yields exactly one row carrying the key's
/// <em>current</em> owner, which is what <see cref="PostprocessAsync" /> compares against the
/// claimant. That is also where both ids in
/// <see cref="DuplicateNaturalKeyException.ExistingStreamId" /> /
/// <see cref="DuplicateNaturalKeyException.ClaimingStreamId" /> come from, with no extra round trip.
/// </para>
/// <para>
/// A claim is legitimate in three cases: the key is unmapped (no conflict at all), the same stream
/// is re-asserting its own mapping (idempotent by design — every event carrying the key rewrites the
/// row), or the stream currently holding it has been archived. That last one is read off
/// <c>mt_streams</c> rather than the lookup row's own <c>is_archived</c>, for the same reason
/// <c>FetchNaturalKeyPlan</c> joins the streams table: nothing on the write side copies the flag
/// across, so the lookup column is only maintained under <c>UseArchivedStreamPartitioning</c>. The
/// ruling refuses a claim on a <em>live</em> key, so an archived stream's key is free to take.
/// </para>
/// <para>
/// Renaming is not a duplicate either, and needs no special handling here: the projection queues a
/// DELETE of this stream's superseded rows ahead of the claim, which retires the old key and frees
/// that identifier for a later stream.
/// </para>
/// </remarks>
internal class NaturalKeyClaimOperation: IStorageOperation
{
    private readonly Type _aggregateType;
    private readonly string _tableName;
    private readonly string _streamsTableName;
    private readonly string _streamColumn;
    private readonly object _streamIdValue;
    private readonly string _tenantId;
    private readonly object _innerValue;
    private readonly bool _isConjoined;
    private readonly bool _useArchivedPartitioning;

    public NaturalKeyClaimOperation(Type aggregateType, string tableName, string streamsTableName,
        string streamColumn, object streamIdValue, string tenantId, object innerValue, bool isConjoined,
        bool useArchivedPartitioning)
    {
        _aggregateType = aggregateType;
        _tableName = tableName;
        _streamsTableName = streamsTableName;
        _streamColumn = streamColumn;
        _streamIdValue = streamIdValue;
        _tenantId = tenantId;
        _innerValue = innerValue;
        _isConjoined = isConjoined;
        _useArchivedPartitioning = useArchivedPartitioning;
    }

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        builder.Append("insert into ");
        builder.Append(_tableName);
        builder.Append(" as nk (natural_key_value, ");
        builder.Append(_streamColumn);
        builder.Append(_isConjoined ? ", tenant_id, is_archived) values (" : ", is_archived) values (");
        builder.AppendParameter(_innerValue);
        builder.Append(", ");
        builder.AppendParameter(_streamIdValue);

        if (_isConjoined)
        {
            builder.Append(", ");
            builder.AppendParameter(_tenantId);
        }

        builder.Append(", false) on conflict (natural_key_value");

        if (_isConjoined)
        {
            builder.Append(", tenant_id");
        }

        if (_useArchivedPartitioning)
        {
            builder.Append(", is_archived");
        }

        builder.Append(") do update set ");
        builder.Append(_streamColumn);
        builder.Append(" = case when nk.");
        builder.Append(_streamColumn);
        builder.Append(" = excluded.");
        builder.Append(_streamColumn);
        builder.Append(" or nk.is_archived or exists (select 1 from ");
        builder.Append(_streamsTableName);
        builder.Append(" s where s.id = nk.");
        builder.Append(_streamColumn);
        builder.Append(" and s.is_archived");

        if (_isConjoined)
        {
            builder.Append(" and s.tenant_id = nk.tenant_id");
        }

        builder.Append(") then excluded.");
        builder.Append(_streamColumn);
        builder.Append(" else nk.");
        builder.Append(_streamColumn);
        builder.Append(" end");

        // is_archived is part of the conflict target under archived partitioning, so a conflicting
        // row necessarily already carries the value being inserted. Everywhere else the existing
        // upsert reset it, and a refused claim leaves it at the false it already held.
        if (!_useArchivedPartitioning)
        {
            builder.Append(", is_archived = false");
        }

        builder.Append(" returning ");
        builder.Append(_streamColumn);
    }

    public Type DocumentType => typeof(StorageFeatures);

    public async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            return;
        }

        var owner = await reader.GetFieldValueAsync<object>(0, token).ConfigureAwait(false);

        if (!owner.Equals(_streamIdValue))
        {
            exceptions.Add(new DuplicateNaturalKeyException(_aggregateType, _innerValue, owner, _streamIdValue));
        }
    }

    public OperationRole Role() => OperationRole.Other;
}
