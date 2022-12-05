using System;
using System.Buffers;

namespace Marten.Util;

internal readonly struct SharedBuffer: IDisposable
{
    private readonly ArraySegment<byte> _buffer;

    private SharedBuffer(ArraySegment<byte> buffer)
    {
        _buffer = buffer;
    }

    public static SharedBuffer RentAndCopy(SOHSkippingStream stream)
    {
        var buffer = ArrayPool<byte>.Shared.Rent((int)stream.Length);
        var totalRead = stream.Read(buffer, 0, buffer.Length);
        return new SharedBuffer(new ArraySegment<byte>(buffer, 0, totalRead));
    }

    public void Dispose()
    {
        if (_buffer.Array != null)
        {
            ArrayPool<byte>.Shared.Return(_buffer.Array);
        }
    }

    public static implicit operator ReadOnlySpan<byte>(SharedBuffer self)
    {
        return self._buffer;
    }

    public static implicit operator ReadOnlySequence<byte>(SharedBuffer self)
    {
        return new ReadOnlySequence<byte>(self._buffer);
    }
}
