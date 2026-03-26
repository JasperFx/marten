using Marten.Events;

namespace Marten.MemoryPackPlugin;

public static class MemoryPackEventSerializationExtensions
{
    /// <summary>
    ///     Enable MemoryPack binary serialization for all event data. This is an "all or nothing"
    ///     mode — every event type must have the [MemoryPackable] attribute.
    ///     The mt_events.data column will use bytea instead of jsonb.
    /// </summary>
    public static IEventStoreOptions UseMemoryPackSerialization(this IEventStoreOptions events)
    {
        events.UseMemoryPackSerialization = true;
        events.BinarySerializer = new MemoryPackEventSerializer();
        return events;
    }
}
