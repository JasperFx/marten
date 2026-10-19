using System;
using System.Collections.Generic;
using System.Linq;
using JasperFx.Events.Projections;
using Marten.Events.Archiving;
using Marten.Linq.SqlGeneration;
using Marten.Storage;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Events;

internal class EventStatement: SelectorStatement
{
    private const string ALL_TENANTS = "~ALL~";
    private readonly IEventStorage _storage;

    public EventStatement(IEventStorage storage, EventGraph events)
    {
        _storage = storage;
        IgnoreSkipped = events.EnableEventSkippingInProjectionsOrSubscriptions;
    }

    public ISqlFragment[] Filters { get; set; } = Array.Empty<ISqlFragment>();

    public EventRange Range { get; set; }

    public long Version { get; set; }

    public DateTimeOffset? Timestamp { get; set; }

    public string TenantId { get; set; } = ALL_TENANTS;

    // Every caller sets exactly one of these two, and a set value is a stream -- Guid.Empty and "" included.
    // They used to double as "no stream predicate" (Guid.Empty, or an empty key), so FetchStreamAsync(Guid.Empty)
    // read every event in the tenant, and CompactStreamAsync then folded and deleted them. Marten refuses both
    // values on Append and StartStream, but BulkInsertEventsAsync, a DCB boundary, legacy data and SQL do not.
    // A key is tracked as assigned rather than read as non-null, because a null key is set too: it names no
    // stream, and must read nothing rather than everything.
    private string _streamKey;
    private bool _hasStreamKey;

    public string StreamKey
    {
        get => _streamKey;
        set
        {
            _streamKey = value;
            _hasStreamKey = true;
        }
    }

    public Guid? StreamId { get; set; }

    public long FromVersion { get; set; }
    public bool IgnoreSkipped { get; }

    protected override void configure(ICommandBuilder builder)
    {
        _storage.Apply(builder);

        var wheres = filters().ToArray();
        switch (wheres.Length)
        {
            case 0:
                break;
            case 1:
                builder.Append(" WHERE ");
                wheres[0].Apply(builder);
                break;
            default:
                var where = CompoundWhereFragment.And(wheres);
                builder.Append(" WHERE ");
                where.Apply(builder);
                break;
        }

        builder.Append(" ORDER BY d.seq_id");
        if (Limit > 0)
        {
            builder.Append(" LIMIT ");
            builder.AppendParameter(Limit);
        }
    }

    private IEnumerable<ISqlFragment> filters()
    {
        yield return IsNotArchivedFilter.Instance;

        if (IgnoreSkipped)
        {
            yield return IsNotSkippedFilter.Instance;
        }

        if (Range != null)
        {
            yield return new WhereFragment("d.seq_id > ?", Range.SequenceFloor);
            yield return new WhereFragment("d.seq_id <= ?", Range.SequenceCeiling);
        }

        if (Version != 0)
        {
            yield return new WhereFragment("d.version <= ?", Version);
        }

        if (FromVersion != 0)
        {
            yield return new WhereFragment("d.version >= ?", FromVersion);
        }

        if (Timestamp.HasValue)
        {
            yield return new WhereFragment("d.timestamp <= ?", Timestamp);
        }

        if (_storage.TenancyStyle == TenancyStyle.Conjoined && TenantId != ALL_TENANTS)
        {
            yield return new WhereFragment("d.tenant_id = ?", TenantId);
        }

        if (StreamId.HasValue)
        {
            yield return new WhereFragment("d.stream_id = ?", StreamId.Value);
        }

        if (_hasStreamKey)
        {
            // stream_id is never null, so no event belongs to a null key
            yield return StreamKey == null
                ? new WhereFragment("false")
                : new WhereFragment("d.stream_id = ?", StreamKey);
        }

        foreach (var filter in Filters) yield return filter;
    }
}
