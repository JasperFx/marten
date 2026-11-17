using System;
using System.Buffers;
using Marten.Events;
using MemoryPack;

namespace Marten.MemoryPack;

/// <summary>
///     MemoryPack-backed <see cref="IEventBinarySerializer"/>. Wire it up
///     either per event type via <c>opts.Events.UseBinarySerializer&lt;TEvent&gt;(new MemoryPackEventSerializer())</c>
///     or store-wide via
///     <see cref="MemoryPackEventSerializerExtensions.UseMemoryPackSerializer"/>
///     to make <c>[BinaryEvent]</c>-marked types resolve to this serializer.
/// </summary>
/// <remarks>
///     Event types must be MemoryPack-serializable — typically a
///     <c>partial</c> type decorated with <c>[MemoryPackable]</c>. See the
///     <see href="https://github.com/Cysharp/MemoryPack">MemoryPack docs</see>.
/// </remarks>
public sealed class MemoryPackEventSerializer: IEventBinarySerializer
{
    public byte[] Serialize(Type type, object data)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        return MemoryPackSerializer.Serialize(type, data);
    }

    public object Deserialize(Type type, byte[] data)
    {
        return Deserialize(type, new ReadOnlySpan<byte>(data));
    }

    /// <summary>
    ///     Serializes straight into <paramref name="destination" /> with MemoryPack's buffer-writer API, so
    ///     Marten can stage <c>mt_events.bdata</c> in a pooled buffer instead of taking a new exact-length array
    ///     per event (jasperfx#1187, marten#5731). Writes the same bytes as <see cref="Serialize(Type, object)" />.
    /// </summary>
    public void Serialize(Type type, object data, IBufferWriter<byte> destination)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (destination is null) throw new ArgumentNullException(nameof(destination));
        MemoryPackSerializer.Serialize(type, destination, data);
    }

    /// <summary>
    ///     Deserializes from a span, so Marten can read <c>mt_events.bdata</c> out of a rented buffer instead of
    ///     copying the column into an exact-length array first (jasperfx#1187, marten#5731).
    /// </summary>
    public object Deserialize(Type type, ReadOnlySpan<byte> data)
    {
        return MemoryPackSerializer.Deserialize(type, data)
            ?? throw new InvalidOperationException(
                $"MemoryPack deserialization returned null for type '{type.FullName}'. " +
                $"Ensure the wire payload was produced by a compatible MemoryPack serializer.");
    }
}
