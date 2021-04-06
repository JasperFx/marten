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
            using (var reader = await connection.ExecuteReaderAsync(command, token))
            {
                if (!await reader.ReadAsync(token)) return default;

                return await selector.ResolveAsync(reader, token);
            }
        }

        internal static async Task<bool> StreamOne(this IManagedConnection connection, NpgsqlCommand command, Stream stream, CancellationToken token)
        {
            using (var reader = (NpgsqlDataReader)await connection.ExecuteReaderAsync(command, token))
            {
                if (!await reader.ReadAsync(token)) return false;

                var ordinal = reader.FieldCount == 1 ? 0 : reader.GetOrdinal("data");

                var source = await reader.GetStreamAsync(ordinal, token);
                await source.CopyStreamSkippingSOHAsync(stream, token);

                return true;
            }
        }

        private static readonly byte[] LeftBracket = Encoding.Default.GetBytes("[");
        private static readonly byte[] RightBracket = Encoding.Default.GetBytes("]");
        private static readonly byte[] Comma = Encoding.Default.GetBytes(",");

#if NET5_0
        internal static ValueTask WriteBytes(this Stream stream, byte[] bytes, CancellationToken token)
        #else
        internal static Task WriteBytes(this Stream stream, byte[] bytes, CancellationToken token)
#endif
        {
#if NET5_0
            return stream.WriteAsync(bytes, token);
#else
            return stream.WriteAsync(bytes, 0, bytes.Length, token);
#endif
        }

        internal static async Task StreamMany(this IManagedConnection connection, NpgsqlCommand command, Stream stream, CancellationToken token)
        {
            await using (var reader = (NpgsqlDataReader)await connection.ExecuteReaderAsync(command, token))
            {
                var ordinal = reader.FieldCount == 1 ? 0 : reader.GetOrdinal("data");

                await stream.WriteBytes(LeftBracket, token);

                if (await reader.ReadAsync(token))
                {
                    var source = await reader.GetStreamAsync(ordinal, token);
                    await source.CopyStreamSkippingSOHAsync(stream, token);
                }

                while (await reader.ReadAsync(token))
                {
                    await stream.WriteBytes(Comma, token);

                    var source = await reader.GetStreamAsync(ordinal, token);
                    await source.CopyStreamSkippingSOHAsync(stream, token);
                }

                await stream.WriteBytes(RightBracket, token);
            }
        }
    }
}
