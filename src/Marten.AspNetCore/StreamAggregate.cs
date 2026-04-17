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

    /// <inheritdoc />
    public Task ExecuteAsync(HttpContext httpContext)
    {
        if (httpContext == null) throw new ArgumentNullException(nameof(httpContext));

        return _useGuid
            ? _session.Events.WriteLatest<T>(_guidId, httpContext, ContentType, OnFoundStatus)
            : _session.Events.WriteLatest<T>(_stringId!, httpContext, ContentType, OnFoundStatus);
    }

    /// <summary>
    /// Populates endpoint metadata so OpenAPI correctly advertises a
    /// <c>200: T</c> and <c>404</c> response for this endpoint.
    /// </summary>
    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));

        builder.Metadata.Add(new ProducesResponseTypeMetadata(
            StatusCodes.Status200OK, typeof(T), new[] { "application/json" }));
        builder.Metadata.Add(new ProducesResponseTypeMetadata(
            StatusCodes.Status404NotFound, typeof(void), Array.Empty<string>()));
    }
}
