using System;
using Marten.Events;
using MemoryPack;

namespace Marten.MemoryPackPlugin;

/// <summary>
///     MemoryPack-based implementation of <see cref="IEventBinarySerializer"/>.
///     Serializes event data to/from binary using MemoryPack.
///     All event types must be decorated with [MemoryPackable].
/// </summary>
public class MemoryPackEventSerializer: IEventBinarySerializer
{
    public byte[] Serialize(Type type, object data)
    {
        return MemoryPackSerializer.Serialize(type, data);
    }

    public object Deserialize(Type type, byte[] data)
    {
        return MemoryPackSerializer.Deserialize(type, data)
               ?? throw new InvalidOperationException(
                   $"MemoryPack deserialization returned null for type '{type.FullName}'");
    }
}
