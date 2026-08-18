#nullable enable
using System;

namespace Marten.Events;

/// <summary>
///     Marks an event type as binary-serialized — its <c>data</c> column in
///     <c>mt_events</c> is populated with the <c>'{}'::jsonb</c> placeholder and
///     the actual payload lives in <c>bdata</c>, serialized by an
///     <see cref="IEventBinarySerializer"/>. See
///     <see href="https://github.com/JasperFx/marten/issues/4515">#4515</see>.
/// </summary>
/// <remarks>
///     <para>
///         The serializer used for an attribute-marked type is resolved at
///         registration time: <c>opts.Events.DefaultBinarySerializer</c> is the
///         fallback when no explicit per-type serializer was wired via
///         <c>opts.Events.UseBinarySerializer&lt;TEvent&gt;(serializer)</c>. If
///         the type is attribute-marked but neither a per-type nor a store-wide
///         serializer is configured, the store will throw at the first append.
///     </para>
///     <para>
///         JSON-serialized events and binary-serialized events coexist in the
///         same table on a per-event-type basis, so applying this attribute to
///         a single event type is a safe in-place change — existing JSON rows
///         continue to read through the JSON path.
///     </para>
/// </remarks>
/// <remarks>
///     <para>
///         jasperfx#669 / JasperFx 2.50.0: the attribute was promoted into
///         <see cref="JasperFx.Events.BinaryEventAttribute"/> so an event type shared by source
///         compiled against several Critter Stack stores can declare its intent once. This one
///         keeps working untouched and no existing user has anything to change. Prefer the JasperFx
///         attribute in new code, and use it in any event type shared across stores.
///     </para>
///     <para>
///         jasperfx#672 / JasperFx 2.51.0 unsealed the promoted attribute specifically so this one
///         could <strong>derive</strong> from it. <see cref="EventGraph.ResolveBinarySerializerFor"/>
///         used to test for both attribute types separately, because a sealed base left no
///         inheritance to lean on (CS0509); now a single lookup for
///         <see cref="JasperFx.Events.BinaryEventAttribute"/> finds this one too, since attribute
///         lookup matches by assignability.
///     </para>
///     <para>
///         Marten is the only store that subclasses the promoted attribute, and only because of the
///         back-compat obligation to the users who already applied this one. A store without a
///         pre-existing attribute should use the JasperFx one directly — a store-namespaced
///         subclass reintroduces exactly what promoting the attribute removed.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class BinaryEventAttribute: JasperFx.Events.BinaryEventAttribute
{
}
