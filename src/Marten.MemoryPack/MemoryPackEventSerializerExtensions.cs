using Marten.Events;

namespace Marten.MemoryPack;

/// <summary>
///     Sugar over <see cref="IEventStoreOptions.DefaultBinarySerializer"/> +
///     <see cref="IEventStoreOptions.UseBinarySerializer{TEvent}"/> for the
///     common case of wiring MemoryPack as the binary serializer for an
///     event store.
/// </summary>
public static class MemoryPackEventSerializerExtensions
{
    /// <summary>
    ///     Register <see cref="MemoryPackEventSerializer"/> as the store-wide
    ///     fallback binary serializer. Combine with the
    ///     <see cref="BinaryEventAttribute"/> on individual event types to opt
    ///     them in without a per-type fluent call.
    /// </summary>
    public static IEventStoreOptions UseMemoryPackSerializer(this IEventStoreOptions events)
    {
        events.DefaultBinarySerializer = new MemoryPackEventSerializer();
        return events;
    }

    /// <summary>
    ///     Register a single event type as MemoryPack-serialized — shorthand for
    ///     <c>events.UseBinarySerializer&lt;TEvent&gt;(new MemoryPackEventSerializer())</c>.
    /// </summary>
    public static IEventStoreOptions UseMemoryPackSerializer<TEvent>(this IEventStoreOptions events)
    {
        return events.UseBinarySerializer<TEvent>(new MemoryPackEventSerializer());
    }
}
