using System;
using System.Linq;
using Marten.Services;
using Shouldly;
using Xunit;

namespace Marten.Testing.Services
{
    public class CharArrayTextWriterTests
    {
        [Fact]
        public void writes_single_char()
        {
            var writer = new CharArrayTextWriter();

            writer.Write('z');

            var written = ToCharSegment(writer);

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

            var written = ToCharSegment(writer);

            written.ShouldBe(chars.Skip(offset).Take(take));
        }

        [Fact]
        public void writes_string()
        {
            var writer = new CharArrayTextWriter();

            writer.Write("test");

            var written = ToCharSegment(writer);

            written.ShouldBe("test");
        }

        [Fact]
        public void writes_characters_beyond_limit()
        {
            var writer = new CharArrayTextWriter();

            var s = new string('a', CharArrayTextWriter.InitialSize) + "b";

            writer.Write(s);

            var written = ToCharSegment(writer);

            written.ShouldBe(s);
        }

        [Fact]
        public void has_offset_reset_when_returned_to_pool_via_single_release()
        {
            var pool = new CharArrayTextWriter.Pool();
            var writer = pool.Lease();
            writer.Write('a');
            pool.Release(writer);

            writer.Size.ShouldBe(0);
        }

        [Fact]
        public void has_offset_reset_when_returned_to_pool_via_collection_release()
        {
            var pool = new CharArrayTextWriter.Pool();
            var writer = pool.Lease();
            writer.Write('a');
            pool.Release(new[] { writer });

            writer.Size.ShouldBe(0);
        }

        static ArraySegment<char> ToCharSegment(CharArrayTextWriter writer)
        {
            return new ArraySegment<char>(writer.Buffer, 0, writer.Size);
        }
    }
}