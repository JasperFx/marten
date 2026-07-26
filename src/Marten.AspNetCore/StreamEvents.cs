using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;

namespace Marten.AspNetCore;

/// <summary>
/// Minimal-API / Wolverine.Http endpoint return value that writes the raw events of a single event
/// stream to the <see cref="HttpContext.Response"/> as a JSON array. Backed by
/// <see cref="FetchStreamPlan"/>, the same query plan that can be batched through
/// <c>IBatchedQuery.QueryByPlan()</c>, and carrying the same optional <c>version</c>,
/// <c>timestamp</c> and <c>fromVersion</c> filters as <c>FetchStreamAsync()</c>.
/// <para>
/// Marten's <c>FetchStream</c> yields an empty list both for a stream that does not exist and for a
/// filter that excludes every event, so the two cannot be told apart here.
/// <see cref="OnEmptyStatus"/> decides which answer the endpoint gives; it defaults to <c>404</c>
/// to match the other single-resource results. Set it to 200 to return an empty JSON array instead
/// — the right choice for an endpoint that pages through a stream with <c>fromVersion</c>, where
/// running off the end is expected rather than exceptional.
/// </para>
/// <para>
/// Elements are <see cref="EventResponse"/>, not Marten's <c>IEvent</c> directly:
/// <c>IEvent.EventType</c> is a <see cref="Type"/> and System.Text.Json refuses to serialize those.
/// Use <c>EventTypeName</c>, Marten's stable event type alias, to discriminate event types client
/// side; the assembly qualified .NET type name is deliberately not written to the wire.
/// </para>
/// </summary>
public sealed class StreamEvents: IResult, IEndpointMetadataProvider
{
    private readonly IQuerySession _session;
    private readonly FetchStreamPlan _plan;

    /// <summary>
    /// Write the events of the Guid-identified stream <paramref name="streamId"/>.
    /// </summary>
    /// <param name="session"></param>
    /// <param name="streamId"></param>
    /// <param name="version">If set, writes events up to and including this version</param>
    /// <param name="timestamp">If set, writes events captured on or before this timestamp</param>
    /// <param name="fromVersion">If set, writes events on or from this version</param>
    public StreamEvents(IQuerySession session, Guid streamId, long version = 0,
        DateTimeOffset? timestamp = null, long fromVersion = 0)
        : this(session, new FetchStreamPlan(streamId, version, timestamp, fromVersion))
    {
    }

    /// <summary>
    /// Write the events of the string-keyed stream <paramref name="streamKey"/>.
    /// </summary>
    /// <param name="session"></param>
    /// <param name="streamKey"></param>
    /// <param name="version">If set, writes events up to and including this version</param>
    /// <param name="timestamp">If set, writes events captured on or before this timestamp</param>
    /// <param name="fromVersion">If set, writes events on or from this version</param>
    public StreamEvents(IQuerySession session, string streamKey, long version = 0,
        DateTimeOffset? timestamp = null, long fromVersion = 0)
        : this(session, new FetchStreamPlan(
            streamKey ?? throw new ArgumentNullException(nameof(streamKey)), version, timestamp, fromVersion))
    {
    }

    /// <summary>
    /// Write the events resolved by an existing <see cref="FetchStreamPlan"/>. Lets a handler build
    /// the plan once and either batch it or return it straight from an endpoint.
    /// </summary>
    public StreamEvents(IQuerySession session, FetchStreamPlan plan)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
    }

    /// <summary>
    /// Status code written when the stream yields at least one event. Defaults to 200.
    /// </summary>
    public int OnFoundStatus { get; init; } = StatusCodes.Status200OK;

    /// <summary>
    /// Status code written when the stream yields no events at all. Defaults to 404.
    /// Set to 200 to write an empty JSON array instead.
    /// </summary>
    public int OnEmptyStatus { get; init; } = StatusCodes.Status404NotFound;

    /// <summary>
    /// Response content type. Defaults to <c>application/json</c>.
    /// </summary>
    public string ContentType { get; init; } = "application/json";

    /// <inheritdoc />
    public Task ExecuteAsync(HttpContext httpContext)
    {
        if (httpContext == null) throw new ArgumentNullException(nameof(httpContext));

        return _session.WriteEvents(_plan, httpContext, ContentType, OnFoundStatus, OnEmptyStatus);
    }

    /// <summary>
    /// Populates endpoint metadata so OpenAPI correctly advertises a
    /// <c>200: EventResponse[]</c> and <c>404</c> response for this endpoint.
    /// </summary>
    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));

        builder.Metadata.Add(new ProducesResponseTypeMetadata(
            StatusCodes.Status200OK, typeof(EventResponse[]), new[] { "application/json" }));
        builder.Metadata.Add(new ProducesResponseTypeMetadata(
            StatusCodes.Status404NotFound, typeof(void), Array.Empty<string>()));
    }
}
