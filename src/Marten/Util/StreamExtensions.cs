using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#nullable enable
namespace Marten.Util
{
    internal static class StreamExtensions
    {
        private const int BufferSize = 81920;

        static readonly Encoding UTF8NoBOM = new UTF8Encoding(false, true);

        public static async Task<Stream> SkipSOHAsync(this Stream stream, CancellationToken token = default)
        {
            var output = new MemoryStream {Position = 0};

            await stream.CopyStreamSkippingSOHAsync(output, token);
            output.Position = 0;

            return output;
        }

        public static async Task CopyStreamSkippingSOHAsync(this Stream input, Stream output, CancellationToken token = default)
        {
            var buffer = new byte[1];

            await input.ReadAsync(buffer, 0, 1, token);
            if (buffer[0] != 1)
            {
                await output.WriteAsync(buffer, 0, buffer.Length, token);
            }

            await input.CopyToAsync(output, 4096, token);
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
    }
}
