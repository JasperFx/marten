using System;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Marten.Linq.CursorPaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;

namespace Marten.AspNetCore;

/// <summary>
/// Minimal-API / Wolverine.Http endpoint return value that streams a keyset
/// ("seek") paginated page of Marten documents, using an opaque continuation
/// cursor instead of <c>Skip</c>/<c>Take</c> offsets. Unlike offset paging, each
/// page costs the same regardless of how deep into the result set it is —
/// making this a good fit for infinite scroll / bulk export scenarios.
/// <para>
/// The supplied <see cref="IQueryable{T}"/> must have an <c>OrderBy</c>/<c>ThenBy</c>
/// chain applied, and the last ordering clause must be on a member guaranteed to
/// be unique across the result set (typically the document identity) so that
/// pagination is deterministic.
/// </para>
/// <para>
/// The response body is a JSON envelope: <c>{"items":[...],"nextCursor":"..."}</c>.
/// <c>nextCursor</c> is <c>null</c> once the end of the result set is reached
/// (i.e. fewer than <see cref="PageSize"/> rows remained). The same value is
/// also written to the <c>Marten-Continuation</c> response header when present,
/// for callers that prefer reading it from headers over the envelope.
/// </para>
/// </summary>
/// <typeparam name="T">The document type contained in the page.</typeparam>
public sealed class StreamPagedByCursor<T>: IResult, IEndpointMetadataProvider where T : notnull
{
    /// <summary>
    /// Name of the response header the continuation cursor is echoed to, in
    /// addition to being included in the JSON envelope.
    /// </summary>
    public const string ContinuationHeaderName = "Marten-Continuation";

    private readonly IQueryable<T> _queryable;
    private readonly string? _cursor;
    private readonly int _pageSize;

    /// <summary>
    /// Create a <see cref="StreamPagedByCursor{T}"/> wrapping a Marten
    /// <see cref="IQueryable{T}"/> that has an <c>OrderBy</c>/<c>ThenBy</c> chain applied.
    /// </summary>
    /// <param name="queryable">
    /// The ordered queryable to page through. The final ordering clause must be
    /// on a member guaranteed unique across the result set (typically the
    /// document identity).
    /// </param>
    /// <param name="cursor">
    /// The continuation cursor from a previous page's response, or <c>null</c>
    /// for the first page.
    /// </param>
    /// <param name="pageSize">The maximum number of documents to return per page.</param>
    public StreamPagedByCursor(IQueryable<T> queryable, string? cursor, int pageSize)
    {
        _queryable = queryable ?? throw new ArgumentNullException(nameof(queryable));
        _cursor = cursor;
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
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        if (httpContext == null) throw new ArgumentNullException(nameof(httpContext));

        var page = await _queryable.ToJsonPageByCursorAsync(_cursor, _pageSize, httpContext.RequestAborted)
            .ConfigureAwait(false);

        if (page.NextCursor != null)
        {
            httpContext.Response.Headers[ContinuationHeaderName] = page.NextCursor;
        }

        var nextCursorJson = page.NextCursor == null
            ? "null"
            : "\"" + page.NextCursor + "\"";

        var envelope = "{\"items\":" + page.ItemsJson + ",\"nextCursor\":" + nextCursorJson + "}";
        var bytes = Encoding.UTF8.GetBytes(envelope);

        httpContext.Response.StatusCode = OnFoundStatus;
        httpContext.Response.ContentType = ContentType;
        httpContext.Response.ContentLength = bytes.Length;

        await httpContext.Response.Body.WriteAsync(bytes, httpContext.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>
    /// Populates endpoint metadata so OpenAPI correctly advertises a
    /// <c>200</c> response for this endpoint.
    /// </summary>
    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));

        builder.Metadata.Add(new ProducesResponseTypeMetadata(
            StatusCodes.Status200OK, typeof(void), new[] { "application/json" }));
    }
}
