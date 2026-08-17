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
///         compiled against several Critter Stack stores can declare its intent once. Marten
///         honors <strong>both</strong> spellings — <see cref="EventGraph.ResolveBinarySerializerFor"/>
///         tests for either attribute — so this one keeps working untouched and no existing user
///         has anything to change. Prefer the JasperFx attribute in new code, and use it in any
///         event type shared across stores.
///     </para>
///     <para>
///         The two are checked separately rather than this one deriving from the core attribute
///         (which would have let a single <c>IsDefined</c> against the base cover both): the
///         promoted <c>JasperFx.Events.BinaryEventAttribute</c> is <c>sealed</c>, so there is no
///         inheritance to lean on.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class BinaryEventAttribute: Attribute
{
}
