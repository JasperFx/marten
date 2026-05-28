#nullable enable
using System;

namespace Marten.Events;

/// <summary>
///     Pluggable binary serializer for event data — addresses
///     <see href="https://github.com/JasperFx/marten/issues/4515">#4515</see>.
///     Allows individual event types to opt out of <c>jsonb</c> serialization
///     in favor of a binary wire format (MemoryPack, MessagePack, etc.).
/// </summary>
/// <remarks>
///     <para>
///         Binary serialization is enabled <strong>per event type</strong>, not
///         store-wide. A store can have JSON events and binary events mixed in
///         the same <c>mt_events</c> table; the row's serialization format is
///         determined by the <c>bdata</c> column being <c>NULL</c> (JSON) or
///         non-null (binary). This makes the feature safe to roll out on an
///         existing store with no migration of existing event data.
///     </para>
///     <para>
///         Opt in by either marking an event type with
///         <see cref="BinaryEventAttribute"/> or registering it through
///         <c>opts.Events.UseBinarySerializer&lt;TEvent&gt;(serializer)</c>.
///         Event types without a per-type serializer fall back to the
///         store-wide <c>opts.Events.DefaultBinarySerializer</c> if one is set.
///     </para>
/// </remarks>
public interface IEventBinarySerializer
{
    /// <summary>
    ///     Serialize an event data instance to bytes.
    /// </summary>
    /// <param name="type">The runtime CLR type of the event data.</param>
    /// <param name="data">The event data to serialize.</param>
    byte[] Serialize(Type type, object data);

    /// <summary>
    ///     Deserialize bytes back into an event data instance.
    /// </summary>
    /// <param name="type">The target CLR type to deserialize into.</param>
    /// <param name="data">The bytes previously produced by <see cref="Serialize"/>.</param>
    object Deserialize(Type type, byte[] data);
}
