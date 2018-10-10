using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Marten.Services;
using Shouldly;
using Xunit;

namespace Marten.Testing.Services
{
    public class CharArrayTextWriterTests
    {
        const int BigEnoughSize = 2048;

        [Fact]
        public void writes_single_char()
        {
            var writer = new CharArrayTextWriter();

            writer.Write('z');

            var written = writer.ToCharSegment();

            written.ShouldBe('z'.ToString());
        }

        [Fact]
        public void writes_char_array()
        {
            var writer = new CharArrayTextWriter();

            var chars = new[] { 'a', 'b', 'c', 'd', 'e', 'f' };

            const int offset = 5;
            const int take = 1;
            writer.Write(chars, offset, take);

            var written = writer.ToCharSegment();

            written.ShouldBe(chars.Skip(offset).Take(take));
        }

        [Fact]
        public void writes_string()
        {
            var writer = new CharArrayTextWriter();

            writer.Write("test");

            var written = writer.ToCharSegment();

            written.ShouldBe("test");
        }

        [Fact]
        public void writes_characters_beyond_limit()
        {
            var writer = new CharArrayTextWriter();

            var s = new string('a', BigEnoughSize) + "b";

            writer.Write(s);

            var written = writer.ToCharSegment();

            written.ShouldBe(s);
        }


        [Fact]
        public void writes_characters_much_beyond_limit()
        {
            var writer = new CharArrayTextWriter();

            var s = new string('a', BigEnoughSize * 8);

            writer.Write(s);

            var written = writer.ToCharSegment();

            written.ShouldBe(s);
        }

        [Fact]
        public void returns_memory_to_pool()
        {
            using (var pool = new Pool())
            {
                using (var writer = new CharArrayTextWriter(pool))
                {
                    writer.Write("s");
                    writer.Write("s");
                    writer.Write("ssss");
                    writer.Write("ssssssssss");

                    pool.Disposed.Count.ShouldBe(3); // last one is still used
                }

                pool.Disposed.Count.ShouldBe(4);
            }
        }

        class Pool : MemoryPool<char>
        {
            public List<char[]> Disposed = new List<char[]>();
            public override IMemoryOwner<char> Rent(int minBufferSize = -1) => new Owner(this, minBufferSize);
            public override int MaxBufferSize => int.MaxValue;
            protected override void Dispose(bool disposing) { }

            void Return(char[] buffer) => Disposed.Add(buffer);

            class Owner : IMemoryOwner<char>
            {
                readonly Pool _pool;
                readonly char[] _buffer;

                public Owner(Pool pool, int minBufferSize)
                {
                    _pool = pool;
                    _buffer = new char[minBufferSize];
                }

                public void Dispose()
                {
                    _pool.Return(_buffer);
                }

                public Memory<char> Memory => new Memory<char>(_buffer);
            }
        }
    }
}