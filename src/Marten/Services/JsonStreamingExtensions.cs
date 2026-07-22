#nullable enable
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Data.Common;
using JasperFx.Core;
using Marten.Linq;
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

    /// <summary>
    /// Streams the first row's <c>data</c> column to <paramref name="stream"/> (as
    /// <see cref="StreamOne"/> does) AND reads the piggy-backed <c>mt_version</c> value
    /// aliased as <see cref="Marten.Linq.SqlGeneration.VersionSelectClause{T}.VersionAlias"/>,
    /// so a single-document JSON stream and its version come back in one round trip.
    /// Returns <c>found = false</c> when the query matched no row, and a null
    /// <c>version</c> when the version column value was SQL NULL.
    /// </summary>
    internal static async Task<(bool found, Guid? version)> StreamOneWithVersion(this DbDataReader reader,
        Stream stream, CancellationToken token)
    {
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            return (false, null);
        }

        var dataOrdinal = reader.GetOrdinal("data");
        await reader.WriteJsonValueAsync(dataOrdinal, stream, token).ConfigureAwait(false);

        var versionOrdinal = reader.GetOrdinal(Marten.Linq.SqlGeneration.VersionSelectClause.VersionAlias);
        Guid? version = await reader.IsDBNullAsync(versionOrdinal, token).ConfigureAwait(false)
            ? null
            : await reader.GetFieldValueAsync<Guid>(versionOrdinal, token).ConfigureAwait(false);

        return (true, version);
    }

    internal static ValueTask WriteBytes(this Stream stream, byte[] bytes, CancellationToken token)
    {
        return stream.WriteAsync(bytes, token);
    }

    /// <summary>
    /// Outcome of streaming one keyset ("cursor") page: the raw items JSON array (byte-identical
    /// to <see cref="StreamMany"/>), the number of documents emitted, the ORDER BY key values of
    /// the last emitted row (for building the next cursor), and whether a further page exists.
    /// </summary>
    internal readonly record struct CursorKeysetReadResult(string ItemsJson, int Count, object?[]? LastKeys, bool HasMore);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, System.Reflection.MethodInfo>
        _getFieldValueMethods = new();

    /// <summary>
    /// Streams the <c>data</c> column of a keyset-paged query as a raw JSON array (identical to
    /// <see cref="StreamMany"/>) while reading the appended <c>cursor_key_N</c> columns for the last
    /// emitted row off the same reader — so the next cursor is built without hydrating any document.
    /// The command is expected to have fetched <paramref name="pageSize"/> + 1 rows; the extra row
    /// (if present) is not emitted and only flags <see cref="CursorKeysetReadResult.HasMore"/>.
    /// </summary>
    internal static async Task<CursorKeysetReadResult> StreamCursorKeyset(this DbDataReader reader,
        Type[] keyTypes, int pageSize, CancellationToken token)
    {
        var stream = Marten.Internal.SharedMemoryStreamManager.GetStream();

        var dataOrdinal = reader.GetOrdinal("data");
        var keyOrdinals = new int[keyTypes.Length];
        for (var i = 0; i < keyTypes.Length; i++)
        {
            keyOrdinals[i] = reader.GetOrdinal($"cursor_key_{i}");
        }

        await stream.WriteBytes(LeftBracket, token).ConfigureAwait(false);

        var count = 0;
        var hasMore = false;
        object?[]? lastKeys = null;

        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            // We fetched pageSize + 1 rows to detect a further page: the (pageSize + 1)th row is
            // never emitted and only proves there is more to come.
            if (count == pageSize)
            {
                hasMore = true;
                break;
            }

            if (count > 0)
            {
                await stream.WriteBytes(Comma, token).ConfigureAwait(false);
            }

            // Read the raw data payload first (WriteJsonValueAsync uses reader.GetStream), then the
            // appended key columns for the same row, which come after data in the SELECT ordinal order.
            await reader.WriteJsonValueAsync(dataOrdinal, stream, token).ConfigureAwait(false);
            lastKeys = await readCursorKeys(reader, keyOrdinals, keyTypes, token).ConfigureAwait(false);

            count++;
        }

        await stream.WriteBytes(RightBracket, token).ConfigureAwait(false);

        stream.Position = 0;
        var itemsJson = await stream.ReadAllTextAsync().ConfigureAwait(false);

        return new CursorKeysetReadResult(itemsJson, count, lastKeys, hasMore);
    }

    private static async Task<object?[]> readCursorKeys(DbDataReader reader, int[] keyOrdinals, Type[] keyTypes,
        CancellationToken token)
    {
        var values = new object?[keyOrdinals.Length];
        for (var i = 0; i < keyOrdinals.Length; i++)
        {
            var ordinal = keyOrdinals[i];
            if (await reader.IsDBNullAsync(ordinal, token).ConfigureAwait(false))
            {
                values[i] = null;
                continue;
            }

            values[i] = readTypedKey(reader, ordinal, keyTypes[i]);
        }

        return values;
    }

    private static object readTypedKey(DbDataReader reader, int ordinal, Type keyType)
    {
        var underlying = Nullable.GetUnderlyingType(keyType) ?? keyType;

        if (underlying.IsEnum)
        {
            // Enum ordering keys can be persisted as their name (EnumStorage.AsString) or their
            // integral (AsInteger). Either way, box the CLR enum so the cursor round-trips the same
            // JSON the pre-#5029 hydrate-then-read path produced.
            var raw = reader.GetValue(ordinal);
            return raw is string name ? Enum.Parse(underlying, name) : Enum.ToObject(underlying, raw);
        }

        // reader.GetFieldValue<underlying>(ordinal): the appended column is cast to the proper PG
        // type by the member's TypedLocator, so Npgsql materializes exactly the CLR key type.
        var method = _getFieldValueMethods.GetOrAdd(underlying, static t =>
            typeof(DbDataReader).GetMethod(nameof(DbDataReader.GetFieldValue))!.MakeGenericMethod(t));

        return method.Invoke(reader, new object[] { ordinal })!;
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
    /// Streams a single "page" of documents plus paging metadata as a JSON envelope of the shape
    /// <c>{"pageNumber":1,"pageSize":25,"totalItemCount":100,"pageCount":4,"hasNextPage":true,"hasPreviousPage":false,"items":[...]}</c>
    /// directly to <paramref name="stream"/>. The reader is expected to have a <c>total_rows</c>
    /// column (added by <c>count(*) OVER()</c>) alongside the document "data" column.
    /// </summary>
    internal static async Task<int> StreamPagedMany(this DbDataReader reader, Stream stream, int pageNumber,
        int pageSize, QueryStatistics statistics, CancellationToken token)
    {
        var ordinal = reader.FieldCount == 1 ? 0 : reader.GetOrdinal("data");

        await stream.WriteBytes(Encoding.UTF8.GetBytes($"{{\"pageNumber\":{pageNumber},\"pageSize\":{pageSize},"), token)
            .ConfigureAwait(false);

        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            statistics.TotalResults = 0;

            await stream.WriteBytes(Encoding.UTF8.GetBytes(
                    $"\"totalItemCount\":0,\"pageCount\":0,\"hasNextPage\":false,\"hasPreviousPage\":{(pageNumber > 1 ? "true" : "false")},\"items\":[]}}"),
                    token)
                .ConfigureAwait(false);

            return 0;
        }

        var totalRowsOrdinal = reader.GetOrdinal("total_rows");
        var totalItemCount = await reader.GetFieldValueAsync<int>(totalRowsOrdinal, token).ConfigureAwait(false);
        statistics.TotalResults = totalItemCount;

        var pageCount = totalItemCount > 0 ? (int)Math.Ceiling(totalItemCount / (double)pageSize) : 0;
        var hasNextPage = pageNumber < pageCount;
        var hasPreviousPage = pageNumber > 1;

        await stream.WriteBytes(Encoding.UTF8.GetBytes(
                $"\"totalItemCount\":{totalItemCount},\"pageCount\":{pageCount},\"hasNextPage\":{(hasNextPage ? "true" : "false")},\"hasPreviousPage\":{(hasPreviousPage ? "true" : "false")},\"items\":["),
                token)
            .ConfigureAwait(false);

        await reader.WriteJsonValueAsync(ordinal, stream, token).ConfigureAwait(false);

        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            await stream.WriteBytes(Comma, token).ConfigureAwait(false);
            await reader.WriteJsonValueAsync(ordinal, stream, token).ConfigureAwait(false);
        }

        await stream.WriteBytes(Encoding.UTF8.GetBytes("]}"), token).ConfigureAwait(false);

        return totalItemCount;
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
