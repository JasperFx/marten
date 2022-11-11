using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Marten.Util;
using Npgsql;
#nullable enable
namespace Marten.Services
{
    internal static class JsonStreamingExtensions
    {
        internal static async Task<int> StreamOne(this NpgsqlDataReader reader, Stream stream, CancellationToken token)
        {
            if (!await reader.ReadAsync(token).ConfigureAwait(false)) return 0;

            var ordinal = reader.FieldCount == 1 ? 0 : reader.GetOrdinal("data");

            var source = await reader.GetStreamAsync(ordinal, token).ConfigureAwait(false);
            await source.CopyStreamSkippingSOHAsync(stream, token).ConfigureAwait(false);

            return 1;
        }

        internal static readonly byte[] LeftBracket = Encoding.Default.GetBytes("[");
        internal static readonly byte[] RightBracket = Encoding.Default.GetBytes("]");
        internal static readonly byte[] Comma = Encoding.Default.GetBytes(",");

        internal static ValueTask WriteBytes(this Stream stream, byte[] bytes, CancellationToken token)
        {
            return stream.WriteAsync(bytes, token);
        }

        internal static async Task<int> StreamMany(this NpgsqlDataReader reader, Stream stream, CancellationToken token)
        {
            var count = 0;
            var ordinal = reader.FieldCount == 1 ? 0 : reader.GetOrdinal("data");

            await stream.WriteBytes(LeftBracket, token).ConfigureAwait(false);

            if (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                count++;
                var source = await reader.GetStreamAsync(ordinal, token).ConfigureAwait(false);
                await source.CopyStreamSkippingSOHAsync(stream, token).ConfigureAwait(false);
            }

            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                count++;
                await stream.WriteBytes(Comma, token).ConfigureAwait(false);

                var source = await reader.GetStreamAsync(ordinal, token).ConfigureAwait(false);
                await source.CopyStreamSkippingSOHAsync(stream, token).ConfigureAwait(false);
            }

            await stream.WriteBytes(RightBracket, token).ConfigureAwait(false);

            return count;
        }
    }
}
