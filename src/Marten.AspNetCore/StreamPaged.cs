using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;

namespace Marten.AspNetCore;

/// <summary>
/// Minimal-API / Wolverine.Http endpoint return value that streams a single "page" of
/// documents from the Linq query, along with paging metadata, directly to the
/// <see cref="HttpContext.Response"/> as a single JSON envelope:
/// <code>
/// {"pageNumber":3,"pageSize":25,"totalItemCount":1207,"pageCount":49,"hasNextPage":true,"hasPreviousPage":true,"items":[...]}
/// </code>
/// Uses <see cref="QueryableExtensions.WritePaged{T}"/> under the hood, which makes exactly
/// one round trip to the database -- the total item count across all pages is retrieved via
/// a <c>count(*) OVER()</c> window function in the same query used to fetch the page of
/// documents, and the matching documents are streamed as raw, already-persisted JSON without
/// a deserialize/serialize round-trip.
/// <para>
/// Unlike <see cref="StreamOne{T}"/>, this type never returns 404: an empty result
/// set yields <c>totalItemCount: 0</c> and an empty <c>items</c> array with status
/// <see cref="OnFoundStatus"/> (default 200).
/// </para>
/// </summary>
/// <typeparam name="T">The document type contained in the page.</typeparam>
public sealed class StreamPaged<T> : IResult, IEndpointMetadataProvider where T : notnull
{
    private readonly IQueryable<T> _queryable;
    private readonly int _pageNumber;
    private readonly int _pageSize;

    /// <summary>
    /// Create a <see cref="StreamPaged{T}"/> wrapping a Marten <see cref="IQueryable{T}"/>.
    /// A single page of matching documents, plus paging metadata, is streamed as a JSON
    /// envelope.
    /// </summary>
    /// <param name="queryable">The Marten Linq query to page over.</param>
    /// <param name="pageNumber">1-based page number.</param>
    /// <param name="pageSize">The number of items per page.</param>
    public StreamPaged(IQueryable<T> queryable, int pageNumber, int pageSize)
    {
        _queryable = queryable ?? throw new ArgumentNullException(nameof(queryable));

        if (pageNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pageNumber), $"pageNumber = {pageNumber}. PageNumber cannot be below 1.");
        }

        if (pageSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), $"pageSize = {pageSize}. PageSize cannot be below 1.");
        }

        _pageNumber = pageNumber;
        _pageSize = pageSize;
    }

    /// <summary>
    /// Status code written with the response. Defaults to 200.
    /// </summary>
    public int OnFoundStatus { get; init; } = StatusCodes.Status200OK;

    /// <summary>
    /// Response content type. Defaults to <c>application/json</c>.
    /// </summary>
    public string ContentType { get; init; } = "application/json";

    /// <inheritdoc />
    public Task ExecuteAsync(HttpContext httpContext)
    {
        if (httpContext == null) throw new ArgumentNullException(nameof(httpContext));
        return _queryable.WritePaged(httpContext, _pageNumber, _pageSize, ContentType, OnFoundStatus);
    }

    /// <summary>
    /// Populates endpoint metadata so OpenAPI correctly advertises a
    /// <c>200</c> response with a paged envelope for this endpoint. No 404 is advertised
    /// because an empty page is a valid response.
    /// </summary>
    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));

        builder.Metadata.Add(new ProducesResponseTypeMetadata(
            StatusCodes.Status200OK, typeof(PagedResult<T>), new[] { "application/json" }));
    }
}

/// <summary>
/// Shape of the JSON envelope streamed by <see cref="StreamPaged{T}"/>. Used only for
/// OpenAPI metadata generation -- <see cref="StreamPaged{T}"/> itself streams the raw JSON
/// directly and never materializes this type.
/// </summary>
/// <typeparam name="T">The document type contained in the page.</typeparam>
public sealed class PagedResult<T>
{
    /// <summary>1-based page number.</summary>
    public int PageNumber { get; set; }

    /// <summary>The number of items per page.</summary>
    public int PageSize { get; set; }

    /// <summary>The total number of items across all pages.</summary>
    public int TotalItemCount { get; set; }

    /// <summary>The total number of pages.</summary>
    public int PageCount { get; set; }

    /// <summary>Whether there is a page after this one.</summary>
    public bool HasNextPage { get; set; }

    /// <summary>Whether there is a page before this one.</summary>
    public bool HasPreviousPage { get; set; }

    /// <summary>The documents on this page.</summary>
    public T[] Items { get; set; } = Array.Empty<T>();
}
