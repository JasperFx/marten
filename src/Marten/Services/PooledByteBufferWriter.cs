#nullable enable
using System;
using System.Buffers;

namespace Marten.Services;

/// <summary>
/// Pooled <see cref="IBufferWriter{T}"/> over <see cref="ArrayPool{T}.Shared"/>. Used as the
/// staging buffer when serializing JSON to UTF-8 bytes for Npgsql parameter binding: the
/// caller writes through <see cref="IBufferWriter{T}"/>, snapshots the written segment, and
/// returns the rented array on <see cref="Dispose"/>. Not thread-safe; intended for a single
/// stack-scoped serialize-then-bind round-trip.
/// </summary>
internal sealed class PooledByteBufferWriter: IBufferWriter<byte>, IDisposable
{
    private byte[] _buffer;
    private int _written;
    private bool _disposed;

    public PooledByteBufferWriter(int initialCapacity = 1024)
    {
        if (initialCapacity < 256) initialCapacity = 256;
        _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
    }

    public int WrittenCount => _written;

    public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _written);

    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);

    public void Advance(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (_written + count > _buffer.Length) throw new InvalidOperationException("Advance past buffer end");
        _written += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsMemory(_written);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsSpan(_written);
    }

    private void EnsureCapacity(int sizeHint)
    {
        if (sizeHint < 0) throw new ArgumentOutOfRangeException(nameof(sizeHint));
        if (sizeHint == 0) sizeHint = 64;
        var remaining = _buffer.Length - _written;
        if (remaining >= sizeHint) return;

        var newSize = Math.Max(_buffer.Length * 2, _written + sizeHint);
        var newer = ArrayPool<byte>.Shared.Rent(newSize);
        Buffer.BlockCopy(_buffer, 0, newer, 0, _written);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = newer;
    }

    /// <summary>
    /// Allocates a fresh <c>byte[]</c> sized exactly to the JSON payload. Used to detach the
    /// payload from the pooled buffer's lifetime — the returned array is safe to hand to
    /// Npgsql which retains the reference past the buffer's <see cref="Dispose"/>.
    /// </summary>
    public byte[] ToSizedArray() => WrittenMemory.ToArray();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ArrayPool<byte>.Shared.Return(_buffer);
    }
}
