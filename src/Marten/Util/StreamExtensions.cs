using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Baseline;

namespace Marten.Util
{
    public static class StreamExtensions
    {
        public static async Task<Stream> SkipSOHAsync(this Stream stream, CancellationToken token = default)
        {
            var arr = new byte[1];
            var currentPosition = stream.Position;
            var firstByte = await stream.ReadAsync(arr, 0, 1, token);
            if (firstByte != 1)
                stream.Seek(currentPosition - 1, SeekOrigin.Begin);

            return stream;
        }

        public static Stream SkipSOH(this Stream stream)
        {
            var arr = new byte[1];
            var currentPosition = stream.Position;
            var firstByte = stream.Read(arr, 0, 1);
            if (firstByte != 1)
                stream.Seek(currentPosition - 1, SeekOrigin.Begin);

            return stream;
        }

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
