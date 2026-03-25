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

    public NaturalKeyProjection(EventGraph events, NaturalKeyDefinition naturalKey)
    {
        _events = events;
        _naturalKey = naturalKey;
        _table = new NaturalKeyTable(events, naturalKey);
        _tableName = _table.Identifier.QualifiedName;
        _isConjoined = events.TenancyStyle == TenancyStyle.Conjoined;
        _isGuid = events.StreamIdentity == StreamIdentity.AsGuid;
    }

    public Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams,
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
                            queueUpsertSql(operations, stream, innerValue);
                        }
                    }
                }
            }
        }

        return Task.CompletedTask;
    }

    private void queueUpsertSql(IDocumentOperations operations, StreamAction stream, object innerValue)
    {
        var streamCol = _isGuid ? "stream_id" : "stream_key";
        object streamId = _isGuid ? (object)stream.Id : stream.Key!;

        if (_isConjoined)
        {
            var sql = $"INSERT INTO {_tableName} (natural_key_value, {streamCol}, tenant_id, is_archived) " +
                      $"VALUES (?, ?, ?, false) " +
                      $"ON CONFLICT (natural_key_value, tenant_id) DO UPDATE SET {streamCol} = ?, is_archived = false";
            operations.QueueSqlCommand(sql, innerValue, streamId, stream.TenantId,
                streamId);
        }
        else
        {
            var sql = $"INSERT INTO {_tableName} (natural_key_value, {streamCol}, is_archived) " +
                      $"VALUES (?, ?, false) " +
                      $"ON CONFLICT (natural_key_value) DO UPDATE SET {streamCol} = ?, is_archived = false";
            operations.QueueSqlCommand(sql, innerValue, streamId, streamId);
        }
    }

    public IEnumerable<ISchemaObject> CreateSchemaObjects(EventGraph events)
    {
        yield return _table;
    }
}
