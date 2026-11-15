#nullable enable
using System;
using System.Buffers;
using System.Data.Common;

namespace Marten.Events;

/// <summary>
///     The <c>mt_events.bdata</c> read path for binary-serialized events (#4515), without the exact-length
///     <c>byte[]</c> per row that <c>GetFieldValue&lt;byte[]&gt;</c> allocates (#5731, jasperfx#1187).
/// </summary>
internal static class BinaryEventPayload
{
    /// <summary>
    ///     Copies the <c>bdata</c> column into a buffer rented from <see cref="ArrayPool{T}.Shared" /> and
    ///     deserializes from a span over it through
    ///     <see cref="JasperFx.Events.IEventBinarySerializer.Deserialize(Type, ReadOnlySpan{byte})" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <c>GetBytes</c> is the read that does not allocate: measured against Npgsql 10 over 200-byte
    ///     payloads, <c>GetFieldValue&lt;byte[]&gt;</c> and <c>GetFieldValue&lt;ReadOnlyMemory&lt;byte&gt;&gt;</c>
    ///     both allocate a fresh array per row, and <c>GetBytes</c> into a rented buffer allocates nothing.
    ///     </para>
    ///     <para>
    ///     It is synchronous, and that is safe on the async read path too: Marten never reads events with
    ///     <c>CommandBehavior.SequentialAccess</c>, so by the time <c>ReadAsync</c> has returned the whole row
    ///     is buffered and <c>GetBytes</c> is a memory copy, not I/O.
    ///     </para>
    ///     <para>
    ///     The buffer goes back to the pool as soon as <c>Deserialize</c> returns. The shared contract says an
    ///     implementation must not hold on to the span, so nothing deserialized can refer to it.
    ///     </para>
    /// </remarks>
    public static object Read(DbDataReader reader, int ordinal, JasperFx.Events.IEventBinarySerializer serializer,
        Type eventType)
    {
        var length = checked((int)reader.GetBytes(ordinal, 0, null, 0, 0));
        var rented = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            var read = (int)reader.GetBytes(ordinal, 0, rented, 0, length);
            return serializer.Deserialize(eventType, new ReadOnlySpan<byte>(rented, 0, read));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
