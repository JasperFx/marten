using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Marten.Services
{
    public sealed class CharArrayTextWriter : TextWriter
    {
        readonly MemoryPool<char> _pool;
        IDisposable _owned;
        Memory<char> _memory;

        int _next;

        public override Encoding Encoding => EncodingValue;
        static readonly Encoding EncodingValue = new UnicodeEncoding(false, false);

        public CharArrayTextWriter() : this(new AllocatingMemoryPool<char>())
        {
        }

        public CharArrayTextWriter(MemoryPool<char> pool)
        {
            _pool = pool;
        }

        public override void Write(char value)
        {
            Ensure(1);
            _memory.Span[_next] = value;
            _next += 1;
        }

        void Ensure(int i)
        {
            var length = _memory.Length;
            if (length == 0)
            {
                var chunk = _pool.Rent(i);
                _owned = chunk;
                _memory = chunk.Memory;

                return;
            }

            var required = _next + i;
            
            if (required < length)
            {
                return;
            }

            while (required >= length)
            {
                length *= 2;
            }

            var newChunk = _pool.Rent(length);
            _memory.CopyTo(newChunk.Memory);

            _owned.Dispose();
            _owned = newChunk;
            _memory = newChunk.Memory;
        }

        public override void Write(char[] buffer, int index, int count)
        {
            var span = new ReadOnlySpan<char>(buffer, index, count);
            Write(span);
        }

        public override void Write(string value)
        {
            var span = value.AsSpan();
            Write(span);
        }

        void Write(ReadOnlySpan<char> span)
        {
            var length = span.Length;

            Ensure(length);
            span.CopyTo(_memory.Span.Slice(_next));
            _next += length;
        }

        public override Task WriteAsync(char value)
        {
            Write(value);
            return Task.CompletedTask;
        }

        public override Task WriteAsync(string value)
        {
            Write(value);
            return Task.CompletedTask;
        }

        public override Task WriteAsync(char[] buffer, int index, int count)
        {
            Write(buffer, index, count);
            return Task.CompletedTask;
        }

        public override Task WriteLineAsync(char value)
        {
            WriteLine(value);
            return Task.CompletedTask;
        }

        public override Task WriteLineAsync(string value)
        {
            WriteLine(value);
            return Task.CompletedTask;
        }

        public override Task WriteLineAsync(char[] buffer, int index, int count)
        {
            WriteLine(buffer, index, count);
            return Task.CompletedTask;
        }

        public override Task FlushAsync()
        {
            return Task.CompletedTask;
        }

        public ArraySegment<char> ToCharSegment()
        {
            // If npgsql was accepting Memory<char> this method could be skipped altogether
            if (MemoryMarshal.TryGetArray<char>(_memory, out var segment))
            {
                return new ArraySegment<char>(segment.Array, segment.Offset, _next);
            }

            // really slow path that might happen with a custom MemoryPool<T>
            return new ArraySegment<char>(_memory.Slice(0, _next).ToArray());
        }

        public void Clear()
        {
            _next = 0;
        }

        protected override void Dispose(bool disposing)
        {
            _owned.Dispose();
        }
    }
}