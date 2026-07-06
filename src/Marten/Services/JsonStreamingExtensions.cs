#nullable enable
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Data.Common;
using Marten.Util;

namespace Marten.Services;

internal static class JsonStreamingExtensions
{
    internal static readonly byte[] LeftBracket = Encoding.Default.GetBytes("[");
    internal static readonly byte[] RightBracket = Encoding.Default.GetBytes("]");
    internal static readonly byte[] Comma = Encoding.Default.GetBytes(",");
    private static readonly byte[] NullLiteral = Encoding.Default.GetBytes("null");

    internal static async Task<int> StreamOne(this DbDataReader reader, Stream stream, CancellationToken token)
    {
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            return 0;
        }

        var ordinal = reader.FieldCount == 1 ? 0 : reader.GetOrdinal("data");

        await reader.WriteJsonValueAsync(ordinal, stream, token).ConfigureAwait(false);

        return 1;
    }

    internal static ValueTask WriteBytes(this Stream stream, byte[] bytes, CancellationToken token)
    {
        return stream.WriteAsync(bytes, token);
    }

    internal static async Task<int> StreamMany(this DbDataReader reader, Stream stream, CancellationToken token)
    {
        var count = 0;
        var ordinal = reader.FieldCount == 1 ? 0 : reader.GetOrdinal("data");

        await stream.WriteBytes(LeftBracket, token).ConfigureAwait(false);

        if (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            count++;
            await reader.WriteJsonValueAsync(ordinal, stream, token).ConfigureAwait(false);
        }

        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            count++;
            await stream.WriteBytes(Comma, token).ConfigureAwait(false);

            await reader.WriteJsonValueAsync(ordinal, stream, token).ConfigureAwait(false);
        }

        await stream.WriteBytes(RightBracket, token).ConfigureAwait(false);

        return count;
    }

    /// <summary>
    /// Writes one row's value at <paramref name="ordinal"/> to <paramref name="stream"/>
    /// as a JSON token.
    ///
    /// For columns that already hold JSON (<c>jsonb</c> / <c>json</c>) — the
    /// document-streaming case — the field is copied byte-for-byte with the
    /// jsonb-binary SOH prefix skipped, exactly as the pre-#4409 path did.
    ///
    /// For everything else — scalar projections like <c>Select(x =&gt; x.Name)</c>
    /// or <c>Select(x =&gt; x.SomeEnum)</c> under <c>EnumStorage.AsString</c> —
    /// the raw text returned by Postgres (<c>FooValue</c>, not <c>"FooValue"</c>)
    /// is not valid JSON when concatenated into an array. Materialize the .NET
    /// value and serialize it via <see cref="JsonSerializer"/> so strings get
    /// quoted and escaped, numerics/bools/datetimes get their JSON literal
    /// representation, and DBNull becomes <c>null</c>.
    /// </summary>
    internal static async Task WriteJsonValueAsync(this DbDataReader reader, int ordinal, Stream stream, CancellationToken token)
    {
        var dataTypeName = reader.GetDataTypeName(ordinal);
        if (dataTypeName is "jsonb" or "json")
        {
            // #4828/Axis B: GetStream is the base DbDataReader method (no Npgsql-typed reader needed).
            await using var source = reader.GetStream(ordinal);
            await source.CopyStreamSkippingSOHAsync(stream, token).ConfigureAwait(false);
            return;
        }

        if (await reader.IsDBNullAsync(ordinal, token).ConfigureAwait(false))
        {
            await stream.WriteBytes(NullLiteral, token).ConfigureAwait(false);
            return;
        }

        var fieldType = reader.GetFieldType(ordinal);
        var value = await reader.GetFieldValueAsync<object>(ordinal, token).ConfigureAwait(false);
        await JsonSerializer.SerializeAsync(stream, value, fieldType, cancellationToken: token).ConfigureAwait(false);
    }
}
