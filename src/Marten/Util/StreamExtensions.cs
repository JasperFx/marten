using System.IO;

namespace Marten.Util
{
    public static class StreamExtensions
    {
        public static StreamReader GetStreamReader(this Stream stream)
        {
            var streamReader = new StreamReader(stream);

            var firstByte = streamReader.Peek();
            if (firstByte == 1)
            {
                streamReader.Read();
            }

            return streamReader;
        }

        public static Stream GetMemoryStreamFromString(string s)
        {
            var stream = new MemoryStream();
            var writer = new StreamWriter(stream);
            writer.Write(s);
            writer.Flush();
            stream.Position = 0;
            return stream;
        }
    }
}
