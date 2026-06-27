using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events.Schema;
using Marten.Storage;
using Weasel.Core;

namespace Marten.Events.Projections;

/// <summary>
/// Auto-registered inline projection that maintains natural key to stream id/key mappings
/// in a dedicated table. This projection upserts the natural key value when matching events
/// are appended and marks entries as archived when streams are archived.
/// </summary>
internal class NaturalKeyProjection: IInlineProjection<IDocumentOperations>, IProjectionSchemaSource
{
    private readonly EventGraph _events;
    private readonly NaturalKeyDefinition _naturalKey;
    private readonly NaturalKeyTable _table;
    private readonly string _tableName;
    private readonly bool _isConjoined;
    private readonly bool _isGuid;
    private readonly bool _useArchivedPartitioning;

    public NaturalKeyProjection(EventGraph events, NaturalKeyDefinition naturalKey)
    {
        _events = events;
        _naturalKey = naturalKey;
        _table = new NaturalKeyTable(events, naturalKey);
        _tableName = _table.Identifier.QualifiedName;
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
                        var rawValue = mapping.Extractor(@event.Data);
                        var innerValue = _naturalKey.Unwrap(rawValue);
                        if (innerValue != null)
                        {
                            queueUpsertSql(operations, stream.Id, stream.Key, stream.TenantId, innerValue);
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
                    var rawValue = mapping.Extractor(@event.Data);
                    var innerValue = _naturalKey.Unwrap(rawValue);
                    if (innerValue != null)
                    {
                        queueUpsertSql(operations, @event.StreamId, @event.StreamKey, @event.TenantId, innerValue);
                    }
                }
            }
        }
    }

    private void queueUpsertSql(IDocumentOperations operations, Guid streamId, string? streamKey,
        string tenantId, object innerValue)
    {
        var streamCol = _isGuid ? "stream_id" : "stream_key";
        object streamIdValue = _isGuid ? (object)streamId : streamKey!;

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
