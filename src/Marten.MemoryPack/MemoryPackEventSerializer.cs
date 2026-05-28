using System;
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
        return MemoryPackSerializer.Deserialize(type, data)
            ?? throw new InvalidOperationException(
                $"MemoryPack deserialization returned null for type '{type.FullName}'. " +
                $"Ensure the wire payload was produced by a compatible MemoryPack serializer.");
    }
}
