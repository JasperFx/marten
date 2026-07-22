using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;

namespace Marten.AspNetCore;

/// <summary>
/// Minimal-API / Wolverine.Http endpoint return value that streams the latest projected
/// JSON of an event-sourced aggregate directly to the <see cref="HttpContext.Response"/>.
/// Uses <see cref="QueryableExtensions.WriteLatest{T}(Marten.Events.IEventStoreOperations, Guid, HttpContext, string, int)"/>
/// under the hood, which pulls the latest aggregate state from the event store without a
/// deserialize/serialize round-trip.
/// <para>
/// Returns HTTP <c>404</c> if no aggregate exists for the supplied id,
/// <see cref="OnFoundStatus"/> (default 200) if it does.
/// </para>
/// <para>
/// <b>StreamAggregate vs StreamOne.</b> Use <see cref="StreamAggregate{T}"/>
/// for event-sourced aggregates — Marten rebuilds (or reads the snapshot of) the
/// latest aggregate state from events before streaming. Use <see cref="StreamOne{T}"/>
/// for regular Marten documents that are stored directly (not event-sourced).
/// </para>
/// </summary>
/// <typeparam name="T">The aggregate type.</typeparam>
public sealed class StreamAggregate<T> : IResult, IEndpointMetadataProvider where T : class
{
    private readonly IDocumentSession _session;
    private readonly Guid _guidId;
    private readonly string? _stringId;
    private readonly bool _useGuid;

    /// <summary>
    /// Stream the latest aggregate state of type <typeparamref name="T"/> for
    /// the aggregate whose stream is identified by <paramref name="id"/>.
    /// </summary>
    public StreamAggregate(IDocumentSession session, Guid id)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _guidId = id;
        _stringId = null;
        _useGuid = true;
    }

    /// <summary>
    /// Stream the latest aggregate state of type <typeparamref name="T"/> for
    /// the aggregate whose stream is identified by string <paramref name="id"/>
    /// (for Marten stores configured with string-keyed streams).
    /// </summary>
    public StreamAggregate(IDocumentSession session, string id)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _stringId = id ?? throw new ArgumentNullException(nameof(id));
        _guidId = Guid.Empty;
        _useGuid = false;
    }

    /// <summary>
    /// Status code written when the aggregate is found. Defaults to 200.
    /// </summary>
    public int OnFoundStatus { get; init; } = StatusCodes.Status200OK;

    /// <summary>
    /// Response content type. Defaults to <c>application/json</c>.
    /// </summary>
    public string ContentType { get; init; } = "application/json";

    /// <summary>
    /// Whether to emit an <c>ETag</c> response header derived from the event stream's
    /// version, and honor an incoming <c>If-None-Match</c> request header by responding
    /// <c>304 Not Modified</c> with an empty body when it matches. The stream version is
    /// cheap to look up (an indexed read against <c>mt_streams</c>) and is fetched before
    /// the aggregate snapshot/fold work, so a cache hit skips that work entirely. Defaults
    /// to <c>true</c>. Set to <c>false</c> to opt out if a consumer's contract cannot
    /// tolerate the extra header.
    /// </summary>
    public bool EmitETag { get; init; } = true;

    /// <inheritdoc />
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        if (httpContext == null) throw new ArgumentNullException(nameof(httpContext));

        if (!EmitETag)
        {
            await writeLatest(httpContext).ConfigureAwait(false);
            return;
        }

        var state = _useGuid
            ? await _session.Events.FetchStreamStateAsync(_guidId, httpContext.RequestAborted).ConfigureAwait(false)
            : await _session.Events.FetchStreamStateAsync(_stringId!, httpContext.RequestAborted).ConfigureAwait(false);

        if (state == null)
        {
            httpContext.Response.StatusCode = 404;
            httpContext.Response.ContentLength = 0;
            return;
        }

        var etag = ETagHelpers.Format(state.Version);

        if (ETagHelpers.IfNoneMatchMatches(httpContext, etag))
        {
            httpContext.Response.StatusCode = StatusCodes.Status304NotModified;
            httpContext.Response.Headers["ETag"] = etag;
            httpContext.Response.ContentLength = 0;
            return;
        }

        httpContext.Response.Headers["ETag"] = etag;

        await writeLatest(httpContext).ConfigureAwait(false);
    }

    private Task writeLatest(HttpContext httpContext)
    {
        return _useGuid
            ? _session.Events.WriteLatest<T>(_guidId, httpContext, ContentType, OnFoundStatus)
            : _session.Events.WriteLatest<T>(_stringId!, httpContext, ContentType, OnFoundStatus);
    }

    /// <summary>
    /// Populates endpoint metadata so OpenAPI correctly advertises a
    /// <c>200: T</c>, <c>304</c>, and <c>404</c> response for this endpoint.
    /// </summary>
    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));

        builder.Metadata.Add(new ProducesResponseTypeMetadata(
            StatusCodes.Status200OK, typeof(T), new[] { "application/json" }));
        builder.Metadata.Add(new ProducesResponseTypeMetadata(
            StatusCodes.Status304NotModified, typeof(void), Array.Empty<string>()));
        builder.Metadata.Add(new ProducesResponseTypeMetadata(
            StatusCodes.Status404NotFound, typeof(void), Array.Empty<string>()));
    }
}
