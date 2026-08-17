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
public interface IEventBinarySerializer: JasperFx.Events.IEventBinarySerializer
{
    // jasperfx#669 / JasperFx 2.50.0: the contract was promoted verbatim into JasperFx.Events so
    // one serializer implementation can serve every Critter Stack store. Both members
    // (byte[] Serialize(Type, object) and object Deserialize(Type, byte[])) now come from the core
    // interface -- this one deliberately declares NOTHING of its own.
    //
    // Keeping the Marten-namespaced name as a derived interface is what makes the move
    // non-breaking in both directions: an existing `class MySerializer : Marten.Events.IEventBinarySerializer`
    // keeps compiling untouched AND now satisfies the core type, while Marten's registration
    // surface (EventGraph.UseBinarySerializer / DefaultBinarySerializer) has been widened to accept
    // the core type so a store-agnostic serializer can be registered without implementing anything
    // Marten-specific.
}
