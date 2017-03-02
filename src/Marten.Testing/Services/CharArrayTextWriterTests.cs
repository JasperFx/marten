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

            var s = new string('a', CharArrayTextWriter.InitialSize) + "b";

            writer.Write(s);

            var written = writer.ToCharSegment();

            written.ShouldBe(s);
        }


        [Fact]
        public void writes_characters_much_beyond_limit()
        {
            var writer = new CharArrayTextWriter();

            var s = new string('a', CharArrayTextWriter.InitialSize * 8);

            writer.Write(s);

            var written = writer.ToCharSegment();

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

        [Fact]
        public void respects_pool_hierarchy()
        {
            var root = new CharArrayTextWriter.Pool();
            CharArrayTextWriter writer1, writer2;

            using (var pool = new CharArrayTextWriter.Pool(root))
            {
                writer1 = pool.Lease();
                pool.Release(writer1);
            }

            using (var pool = new CharArrayTextWriter.Pool(root))
            {
                writer2 = pool.Lease();
                pool.Release(writer2);
            }

            writer2.ShouldBe(writer1);
        }
    }
}