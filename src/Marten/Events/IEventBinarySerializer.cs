using System;

namespace Marten.Events;

/// <summary>
///     Interface for binary event serialization (e.g., MemoryPack).
///     When configured, all events are stored as binary (bytea) instead of JSON (jsonb).
///     This is an "all or nothing" mode — every event type must support binary serialization.
/// </summary>
public interface IEventBinarySerializer
{
    /// <summary>
    ///     Serialize an event data object to a binary byte array.
    /// </summary>
    /// <param name="type">The runtime type of the event data</param>
    /// <param name="data">The event data object to serialize</param>
    /// <returns>The binary representation of the event data</returns>
    byte[] Serialize(Type type, object data);

    /// <summary>
    ///     Deserialize a binary byte array back to an event data object.
    /// </summary>
    /// <param name="type">The target type to deserialize into</param>
    /// <param name="data">The binary data to deserialize</param>
    /// <returns>The deserialized event data object</returns>
    object Deserialize(Type type, byte[] data);
}
