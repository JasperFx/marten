using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;

namespace Marten.AspNetCore;

/// <summary>
/// Minimal-API / Wolverine.Http endpoint return value that writes the high level metadata of a
/// single event stream — Marten's <c>StreamState</c> — to the <see cref="HttpContext.Response"/>
/// as JSON. Backed by <see cref="FetchStreamStatePlan"/>, the same query plan that can be batched
/// through <c>IBatchedQuery.QueryByPlan()</c>.
/// <para>
/// Returns HTTP <c>404</c> when the stream does not exist, <see cref="OnFoundStatus"/> (default 200)
/// when it does.
/// </para>
/// <para>
/// The response body is a <see cref="StreamStateResponse"/>, not Marten's <c>StreamState</c>
/// directly: <c>StreamState.AggregateType</c> is a <see cref="Type"/> and System.Text.Json refuses
/// to serialize those, so the aggregate type is projected down to its simple name.
/// </para>
/// <para>
/// <b>StreamEventState vs StreamAggregate.</b> Use <see cref="StreamEventState"/> when you want the
/// stream's <i>metadata</i> — version, timestamps, archived flag. Use <see cref="StreamAggregate{T}"/>
/// when you want the projected aggregate <i>state</i> built from the stream's events.
/// </para>
/// </summary>
public sealed class StreamEventState: IResult, IEndpointMetadataProvider
{
    private readonly IQuerySession _session;
    private readonly FetchStreamStatePlan _plan;

    /// <summary>
    /// Write the stream metadata for the Guid-identified stream <paramref name="streamId"/>.
    /// </summary>
    public StreamEventState(IQuerySession session, Guid streamId)
        : this(session, new FetchStreamStatePlan(streamId))
    {
    }

    /// <summary>
    /// Write the stream metadata for the string-keyed stream <paramref name="streamKey"/>.
    /// </summary>
    public StreamEventState(IQuerySession session, string streamKey)
        : this(session, new FetchStreamStatePlan(
            streamKey ?? throw new ArgumentNullException(nameof(streamKey))))
    {
    }

    /// <summary>
    /// Write the stream metadata resolved by an existing <see cref="FetchStreamStatePlan"/>. Lets a
    /// handler build the plan once and either batch it or return it straight from an endpoint.
    /// </summary>
    public StreamEventState(IQuerySession session, FetchStreamStatePlan plan)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
    }

    /// <summary>
    /// Status code written when the stream is found. Defaults to 200.
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

        return _session.WriteStreamState(_plan, httpContext, ContentType, OnFoundStatus);
    }

    /// <summary>
    /// Populates endpoint metadata so OpenAPI correctly advertises a
    /// <c>200: StreamStateResponse</c> and <c>404</c> response for this endpoint.
    /// </summary>
    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));

        builder.Metadata.Add(new ProducesResponseTypeMetadata(
            StatusCodes.Status200OK, typeof(StreamStateResponse), new[] { "application/json" }));
        builder.Metadata.Add(new ProducesResponseTypeMetadata(
            StatusCodes.Status404NotFound, typeof(void), Array.Empty<string>()));
    }
}
