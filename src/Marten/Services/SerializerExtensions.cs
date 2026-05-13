#nullable enable
namespace Marten.Services;

/// <summary>
/// Shared helpers for <see cref="ISerializer"/> that wrap the common UTF-8
/// serialization patterns (pooled buffer + sized byte[]). These are intentionally
/// static methods rather than instance ones on <see cref="ISerializer"/> so the
/// public interface stays minimal.
/// </summary>
public static class SerializerExtensions
{
    /// <summary>
    /// Serialize <paramref name="value"/> directly to a sized UTF-8 <c>byte[]</c>
    /// using the serializer's <see cref="ISerializer.WriteTo"/> path. Avoids the
    /// intermediate string allocation that <see cref="ISerializer.ToJson"/> emits.
    /// Used by the event-store / bulk-loader codegen + any non-codegen call site
    /// that needs to hand UTF-8 bytes to Npgsql.
    /// </summary>
    public static byte[] SerializeToUtf8(this ISerializer serializer, object? value)
    {
        using var buffer = new PooledByteBufferWriter();
        serializer.WriteTo(buffer, value);
        return buffer.ToSizedArray();
    }
}
