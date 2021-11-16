using System.Data;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq.Selectors;
using Marten.Util;
using Npgsql;
#nullable enable
namespace Marten.Services
{
    internal static class ManagedConnectionExtensions
    {
        internal static T? LoadOne<T>(this IManagedConnection connection, NpgsqlCommand command, ISelector<T> selector)
        {
            using (var reader = connection.ExecuteReader(command))
            {
                if (!reader.Read()) return default;

                return selector.Resolve(reader);
            }
        }

        internal static async Task<T?> LoadOneAsync<T>(this IManagedConnection connection, NpgsqlCommand command, ISelector<T> selector, CancellationToken token)
        {
            using var reader = await connection.ExecuteReaderAsync(command, token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false)) return default;

            return await selector.ResolveAsync(reader, token).ConfigureAwait(false);
        }

        internal static async Task<bool> StreamOne(this IManagedConnection connection, NpgsqlCommand command, Stream stream, CancellationToken token)
        {
            using var reader = (NpgsqlDataReader)await connection.ExecuteReaderAsync(command, token).ConfigureAwait(false);
            return (await StreamOne(reader, stream, token).ConfigureAwait(false) == 1);
        }

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

#if NET
        internal static ValueTask WriteBytes(this Stream stream, byte[] bytes, CancellationToken token)
        #else
        internal static Task WriteBytes(this Stream stream, byte[] bytes, CancellationToken token)
#endif
        {
#if NET
            return stream.WriteAsync(bytes, token);
#else
            return stream.WriteAsync(bytes, 0, bytes.Length, token);
#endif
        }

        internal static async Task<int> StreamMany(this IManagedConnection connection, NpgsqlCommand command, Stream stream, CancellationToken token)
        {
            using var reader = (NpgsqlDataReader)await connection.ExecuteReaderAsync(command, token).ConfigureAwait(false);

            return await reader.StreamMany(stream, token).ConfigureAwait(false);
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
