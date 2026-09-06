using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events.Operations;
using Marten.Events.Schema;
using Marten.Storage;
using Weasel.Core;

namespace Marten.Events.Projections;

/// <summary>
/// Auto-registered inline projection that maintains natural key to stream id/key mappings in a
/// dedicated table, writing the natural key value when a matching event is appended.
/// </summary>
/// <remarks>
/// It does <em>not</em> mark entries archived when a stream is archived, which this comment used to
/// claim (#5344). Nothing on the write side ever did: archival runs through
/// <c>mt_archive_stream</c>, which has never touched <c>mt_natural_key_X</c>, so the row's own
/// <c>is_archived</c> column is only maintained under <c>UseArchivedStreamPartitioning</c>, where
/// the cascading foreign key moves it along with its stream. Readers get the flag off
/// <c>mt_streams</c> instead — see <c>FetchNaturalKeyPlan.BuildNaturalKeyToStreamQuery</c>.
/// </remarks>
internal class NaturalKeyProjection: IInlineProjection<IDocumentOperations>, IProjectionSchemaSource
{
    private readonly EventGraph _events;
    private readonly NaturalKeyDefinition _naturalKey;
    private readonly NaturalKeyTable _table;
    private readonly string _tableName;
    private readonly string _streamsTableName;
    private readonly bool _isConjoined;
    private readonly bool _isGuid;
    private readonly bool _useArchivedPartitioning;

    public NaturalKeyProjection(EventGraph events, NaturalKeyDefinition naturalKey)
    {
        _events = events;
        _naturalKey = naturalKey;
        _table = new NaturalKeyTable(events, naturalKey);
        _tableName = _table.Identifier.QualifiedName;
        _streamsTableName = $"{events.DatabaseSchemaName}.{StreamsTable.TableName}";
        _useArchivedPartitioning = events.UseArchivedStreamPartitioning;
        _isConjoined = events.TenancyStyle == TenancyStyle.Conjoined;
        _isGuid = events.StreamIdentity == StreamIdentity.AsGuid;
    }

    public Task ApplyAsync(IDocumentOperations operations, IEnumerable<StreamAction> streams,
        CancellationToken cancellation)
    {
        foreach (var stream in streams)
        {
            // Process events that carry natural key values
            foreach (var @event in stream.Events)
            {
                foreach (var mapping in _naturalKey.EventMappings)
                {
                    if (mapping.EventType.IsAssignableFrom(@event.Data.GetType()))
                    {
                        var rawValue = mapping.Extractor(@event);
                        var innerValue = _naturalKey.Unwrap(rawValue);
                        if (innerValue != null)
                        {
                            queueClaim(operations, stream.Id, stream.Key, stream.TenantId, innerValue);
                        }
                    }
                }
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// #4788: rebuild-time counterpart to <see cref="ApplyAsync"/>. The async-daemon rebuild path
    /// replays already-persisted events without appending streams, so <c>ApplyAsync</c>'s
    /// stream-driven dispatch never fires and the <c>mt_natural_key_X</c> table stays empty after
    /// teardown. This entry-point feeds raw <see cref="IEvent"/>s straight through the same
    /// upsert SQL builder, pulling stream id/key + tenant id off the event itself (events written
    /// to <c>mt_events</c> always carry these). Called from <c>StartProjectionBatchAsync</c> per
    /// rebuild page, with <paramref name="operations"/> routed through
    /// <see cref="ProjectionBatch.SessionForTenant"/> so the SQL flushes into the projection batch
    /// rather than the bare session's unit-of-work.
    /// </summary>
    internal void QueueUpsertsForEvents(IDocumentOperations operations, IEnumerable<IEvent> events)
    {
        foreach (var @event in events)
        {
            foreach (var mapping in _naturalKey.EventMappings)
            {
                if (mapping.EventType.IsAssignableFrom(@event.Data.GetType()))
                {
                    var rawValue = mapping.Extractor(@event);
                    var innerValue = _naturalKey.Unwrap(rawValue);
                    if (innerValue != null)
                    {
                        queueUpsertSql(operations, @event.StreamId, @event.StreamKey, @event.TenantId, innerValue);
                    }
                }
            }
        }
    }

    /// <summary>
    /// #5041: a stream has exactly one <em>current</em> natural key value, but the writes below only
    /// ever insert. When an event changes the key, the row carrying the previous value stays behind
    /// pointing at the same stream, so the table accumulates one dead row per rename and the retired
    /// key keeps occupying its slot in the primary key. Retire those rows first — scoped to this
    /// stream (and tenant, when conjoined) so a key legitimately owned by some other stream is never
    /// touched. Queued ahead of the write, and the batch preserves order, so a Create-then-rename
    /// inside a single batch still lands on the newest value.
    /// </summary>
    private void queueRetirementOfSupersededKeys(IDocumentOperations operations, string streamCol,
        object streamIdValue, string tenantId, object innerValue)
    {
        if (_isConjoined)
        {
            operations.QueueSqlCommand(
                $"DELETE FROM {_tableName} WHERE tenant_id = ? AND {streamCol} = ? AND natural_key_value <> ?",
                tenantId, streamIdValue, innerValue);
        }
        else
        {
            operations.QueueSqlCommand(
                $"DELETE FROM {_tableName} WHERE {streamCol} = ? AND natural_key_value <> ?",
                streamIdValue, innerValue);
        }
    }

    /// <summary>
    /// #5344: the append-path claim, which refuses a key already mapped to a different live stream
    /// rather than repointing the row at the newcomer. The retiring DELETE below is shared with the
    /// rebuild path; only the write itself differs.
    /// </summary>
    private void queueClaim(IDocumentOperations operations, Guid streamId, string? streamKey,
        string tenantId, object innerValue)
    {
        var streamCol = _isGuid ? "stream_id" : "stream_key";
        object streamIdValue = _isGuid ? (object)streamId : streamKey!;

        queueRetirementOfSupersededKeys(operations, streamCol, streamIdValue, tenantId, innerValue);

        operations.QueueOperation(new NaturalKeyClaimOperation(
            _naturalKey.AggregateType,
            _tableName,
            _streamsTableName,
            streamCol,
            streamIdValue,
            tenantId,
            innerValue,
            _isConjoined,
            _useArchivedPartitioning));
    }

    /// <summary>
    /// The rebuild-path write, which stays last-writer-wins.
    /// </summary>
    /// <remarks>
    /// A rebuild replays events that were already accepted at append time, where the claim above is
    /// what enforces uniqueness. Re-adjudicating them here would turn a pre-existing data condition
    /// — two live streams sharing a key, which older Marten permitted precisely because the upsert
    /// repointed — into an unrecoverable daemon failure, with no caller present to correct the key
    /// derivation the ruling blames. Replay order also preserves renames: a stream's superseded rows
    /// are retired before the next claim, so the run ends on the same mapping the append path built.
    /// </remarks>
    private void queueUpsertSql(IDocumentOperations operations, Guid streamId, string? streamKey,
        string tenantId, object innerValue)
    {
        var streamCol = _isGuid ? "stream_id" : "stream_key";
        object streamIdValue = _isGuid ? (object)streamId : streamKey!;

        queueRetirementOfSupersededKeys(operations, streamCol, streamIdValue, tenantId, innerValue);

        // When UseArchivedStreamPartitioning is on, is_archived is part of the PK
        // and must be included in the ON CONFLICT clause
        if (_isConjoined && _useArchivedPartitioning)
        {
            var sql = $"INSERT INTO {_tableName} (natural_key_value, {streamCol}, tenant_id, is_archived) " +
                      $"VALUES (?, ?, ?, false) " +
                      $"ON CONFLICT (natural_key_value, tenant_id, is_archived) DO UPDATE SET {streamCol} = ?";
            operations.QueueSqlCommand(sql, innerValue, streamIdValue, tenantId, streamIdValue);
        }
        else if (_isConjoined)
        {
            var sql = $"INSERT INTO {_tableName} (natural_key_value, {streamCol}, tenant_id, is_archived) " +
                      $"VALUES (?, ?, ?, false) " +
                      $"ON CONFLICT (natural_key_value, tenant_id) DO UPDATE SET {streamCol} = ?, is_archived = false";
            operations.QueueSqlCommand(sql, innerValue, streamIdValue, tenantId, streamIdValue);
        }
        else if (_useArchivedPartitioning)
        {
            var sql = $"INSERT INTO {_tableName} (natural_key_value, {streamCol}, is_archived) " +
                      $"VALUES (?, ?, false) " +
                      $"ON CONFLICT (natural_key_value, is_archived) DO UPDATE SET {streamCol} = ?";
            operations.QueueSqlCommand(sql, innerValue, streamIdValue, streamIdValue);
        }
        else
        {
            var sql = $"INSERT INTO {_tableName} (natural_key_value, {streamCol}, is_archived) " +
                      $"VALUES (?, ?, false) " +
                      $"ON CONFLICT (natural_key_value) DO UPDATE SET {streamCol} = ?, is_archived = false";
            operations.QueueSqlCommand(sql, innerValue, streamIdValue, streamIdValue);
        }
    }

    public IEnumerable<ISchemaObject> CreateSchemaObjects(EventGraph events)
    {
        yield return _table;
    }
}
