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

            var written = writer.ToRawArraySegment();

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

            var written = writer.ToRawArraySegment();

            written.ShouldBe(chars.Skip(offset).Take(take));
        }

        [Fact]
        public void writes_string()
        {
            var writer = new CharArrayTextWriter();

            writer.Write("test");

            var written = writer.ToRawArraySegment();

            written.ShouldBe("test");
        }

        [Fact]
        public void writes_characters_beyond_limit()
        {
            var writer = new CharArrayTextWriter();

            var s = new string('a', CharArrayTextWriter.InitialSize) + "b";

            writer.Write(s);

            var written = writer.ToRawArraySegment();

            written.ShouldBe(s);
        }
    }
}