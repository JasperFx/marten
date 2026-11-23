#nullable enable
using JasperFx.Events.Fetching;

namespace Marten.Events.Fetching;

/// <summary>
/// A session's write-back to the aggregate write cache, which also points every natural key of an aggregate it
/// publishes at the stream the aggregate is cached under. That is what lets a fetch by natural key claim an entry
/// written back by any fetch, by stream id or by key.
/// </summary>
internal sealed class MartenAggregateWriteCacheSession: AggregateWriteCacheSession
{
    private readonly EventGraph _events;

    public MartenAggregateWriteCacheSession(EventGraph events)
    {
        _events = events;
    }

    protected override void OnPublished(AggregateCacheKey key, object aggregate)
    {
        _events.NaturalKeyStreams.Register(key, aggregate);
    }
}
