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

        public static SOHSkippingStream ToSOHSkippingStream(this Stream stream)
        {
            return  new SOHSkippingStream(stream);
        }

        public static async Task CopyStreamSkippingSOHAsync(this Stream input, Stream output, CancellationToken token = default)
        {
            var sohSkippingStream = new SOHSkippingStream(input);
            await sohSkippingStream.CopyToAsync(output, 4096, token).ConfigureAwait(false);
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
