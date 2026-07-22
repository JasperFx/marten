#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Internal.Storage;
using Marten.Linq;
using Marten.Linq.Parsing;

namespace Marten.Linq.CursorPaging;

/// <summary>
/// Keyset (seek) pagination support for Marten Linq queries: given an
/// <see cref="IQueryable{T}"/> with an <c>OrderBy</c>/<c>ThenBy</c> chain applied,
/// fetch pages at constant cost regardless of depth, using an opaque continuation
/// cursor instead of <c>Skip</c>/<c>Take</c> offsets.
/// </summary>
public static class CursorPagingQueryableExtensions
{
    /// <summary>
    /// Execute <paramref name="queryable"/> as a keyset-paginated page of results.
    /// </summary>
    /// <param name="queryable">
    /// A Marten Linq queryable with at least one <c>OrderBy</c>/<c>ThenBy</c> clause
    /// applied. The last ordering clause must be on a member guaranteed to be
    /// unique across the result set (typically the document identity) so that
    /// pagination is deterministic.
    /// </param>
    /// <param name="cursor">
    /// The opaque continuation cursor from a previous call's
    /// <see cref="CursorPageResult.NextCursor"/>, or <c>null</c>/empty for the
    /// first page.
    /// </param>
    /// <param name="pageSize">The maximum number of documents to return in this page.</param>
    /// <param name="token"></param>
    /// <typeparam name="T"></typeparam>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="pageSize"/> is not positive.</exception>
    /// <exception cref="InvalidOperationException">
    /// If <paramref name="queryable"/> has no OrderBy clause, or the terminal
    /// OrderBy/ThenBy clause is not on a member guaranteed to be unique.
    /// </exception>
    /// <exception cref="ArgumentException">If <paramref name="cursor"/> is malformed or does not match the ordering.</exception>
    public static async Task<CursorPageResult> ToJsonPageByCursorAsync<T>(
        this IQueryable<T> queryable,
        string? cursor,
        int pageSize,
        CancellationToken token = default) where T : notnull
    {
        if (queryable == null) throw new ArgumentNullException(nameof(queryable));
        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, "Page size must be greater than zero.");
        }

        var martenQueryable = queryable.As<MartenLinqQueryable<T>>();
        var session = martenQueryable.Session;

        var orderings = CursorPagination.ParseOrderings(queryable.Expression);
        if (orderings.Count == 0)
        {
            throw new InvalidOperationException(
                $"StreamPagedByCursor<{typeof(T).Name}> requires the supplied queryable to have at least one " +
                "OrderBy/OrderByDescending clause so that keyset pagination is deterministic.");
        }

        CursorPagination.ValidateTerminalKeyIsUnique<T>(orderings[^1]);

        var working = queryable;

        if (!string.IsNullOrEmpty(cursor))
        {
            var values = CursorPagination.DecodeCursor(cursor!, orderings);
            var predicate = CursorPagination.BuildSeekPredicate<T>(orderings, values);
            working = working.Where(predicate);
        }

        // Fetch one extra row so we can tell whether a further page exists.
        working = working.Take(pageSize + 1);

        // Resolve each ordering to the SAME SQL locator the OrderBy chain uses, and append it to the
        // page query as a projected column (the "extra column riding the page query" technique
        // StreamPaged uses for count(*) OVER()). This lets us read the next cursor's key values off
        // the reader instead of hydrating each document.
        var queryMembers = ((ILinqDocumentStorage)session.StorageFor(typeof(T))).QueryMembers;
        var keyColumns = new string[orderings.Count];
        var keyTypes = new Type[orderings.Count];
        for (var i = 0; i < orderings.Count; i++)
        {
            var member = queryMembers.MemberFor(orderings[i].Selector.Body);
            keyColumns[i] = $"{member.TypedLocator} as cursor_key_{i}";
            keyTypes[i] = orderings[i].KeyType;
        }

        return await martenQueryable.MartenProvider
            .StreamCursorPage(working.As<MartenLinqQueryable<T>>().Expression, keyColumns, keyTypes, pageSize, token)
            .ConfigureAwait(false);
    }
}
