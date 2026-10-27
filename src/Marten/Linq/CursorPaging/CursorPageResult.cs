using System;
using System.IO;
using System.Threading.Tasks;

namespace Marten.Linq.CursorPaging;

/// <summary>
/// The result of a keyset (seek) paginated query executed through
/// <see cref="CursorPagingQueryableExtensions.ToJsonPageByCursorAsync{T}"/>.
/// </summary>
public sealed class CursorPageResult
{
    public CursorPageResult(string itemsJson, int count, string? nextCursor)
    {
        ItemsJson = itemsJson;
        Count = count;
        NextCursor = nextCursor;
    }

    /// <summary>
    /// The raw JSON array text (e.g. <c>[{"Id":...},{"Id":...}]</c>) for the
    /// documents in this page. Will be <c>"[]"</c> for an empty page.
    /// </summary>
    public string ItemsJson { get; }

    /// <summary>
    /// The number of documents contained in <see cref="ItemsJson"/>.
    /// </summary>
    public int Count { get; }

    /// <summary>
    /// The opaque continuation cursor to request the next page, or <c>null</c>
    /// if this page reached the end of the result set (fewer rows than the
    /// requested page size were available).
    /// </summary>
    public string? NextCursor { get; }
}

/// <summary>
/// One keyset page with its items still as the UTF-8 JSON array Marten read off the reader, so a caller
/// writing it out (<c>StreamPagedByCursor</c>) copies the bytes instead of decoding them to a string and
/// encoding them back. The items stream is pooled: dispose the page to return its buffers.
/// </summary>
internal sealed class CursorPageUtf8: IAsyncDisposable
{
    public CursorPageUtf8(MemoryStream itemsUtf8, int count, string? nextCursor)
    {
        ItemsUtf8 = itemsUtf8;
        Count = count;
        NextCursor = nextCursor;
    }

    /// <summary>The items JSON array as UTF-8, positioned at its start.</summary>
    public MemoryStream ItemsUtf8 { get; }

    public int Count { get; }

    public string? NextCursor { get; }

    public ValueTask DisposeAsync() => ItemsUtf8.DisposeAsync();
}
