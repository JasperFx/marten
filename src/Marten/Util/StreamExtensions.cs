using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Util
{
    public static class StreamExtensions
    {
        public static async Task<Stream> SkipSOHAsync(this Stream stream, CancellationToken token = default)
        {
            // This could be optimised with some delegated stream to not need to load whole stream at once
            // unfortunately sometimes (probably for bigger JSON) stream starts with SOH (0x01)
            // we need to skip it to be able to deserialize it.
            // Stream returned by Npgsql even if it's marked as seekable - it's not, so we need to get it to memory
            // It's not a big issue, as we always have to load it fully, but it could use less memory at once
            var output = new MemoryStream();
            await stream.CopyToAsync(output, 4096, token);
            output.Position = 0;

            var arr = new byte[1];

            await output.ReadAsync(arr, 0, 1, token);
            if (arr[0] != 1)
                output.Seek(0, SeekOrigin.Begin);

            return output;
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
