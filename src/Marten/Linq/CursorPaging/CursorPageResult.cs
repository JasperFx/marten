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
