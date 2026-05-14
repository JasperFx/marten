#nullable enable
using System;
using System.Buffers;
using System.IO;

namespace Marten.Services;

/// <summary>
/// Write-only <see cref="Stream"/> facade over an <see cref="IBufferWriter{T}"/>. Used so
/// Newtonsoft.Json's <c>JsonTextWriter</c>, which writes through a <see cref="TextWriter"/>
/// on top of a <see cref="Stream"/>, can deliver UTF-8 bytes directly into a pooled buffer
/// without an intermediate <c>MemoryStream</c> or <c>StringBuilder</c>.
/// </summary>
internal sealed class BufferWriterStream: Stream
{
    private readonly IBufferWriter<byte> _writer;

    public BufferWriterStream(IBufferWriter<byte> writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));
        if (count <= 0) return;
        var span = _writer.GetSpan(count);
        buffer.AsSpan(offset, count).CopyTo(span);
        _writer.Advance(count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty) return;
        var span = _writer.GetSpan(buffer.Length);
        buffer.CopyTo(span);
        _writer.Advance(buffer.Length);
    }

    public override void WriteByte(byte value)
    {
        var span = _writer.GetSpan(1);
        span[0] = value;
        _writer.Advance(1);
    }
}
