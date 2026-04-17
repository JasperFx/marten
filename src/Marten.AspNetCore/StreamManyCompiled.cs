using System;
using System.Reflection;
using System.Threading.Tasks;
using Marten.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;

namespace Marten.AspNetCore;

/// <summary>
/// Minimal-API / Wolverine.Http endpoint return value that streams the JSON array
/// produced by a Marten <see cref="ICompiledQuery{TDoc, TOut}"/> (typically an
/// <see cref="ICompiledListQuery{TDoc, TOut}"/>) directly to the
/// <see cref="HttpContext.Response"/>. Uses
/// <see cref="QueryableExtensions.WriteArray{TDoc, TOut}"/> under the hood — the JSON
/// array is written straight to the response stream without a deserialize/serialize
/// round-trip.
/// <para>
/// Unlike <see cref="StreamOne{TDoc, TOut}"/>, this type never returns 404: an empty
/// result set yields an empty JSON array (<c>[]</c>) with status
/// <see cref="OnFoundStatus"/> (default 200).
/// </para>
/// <para>
/// This is the <see cref="ICompiledQuery{TDoc, TOut}"/> overload. Use
/// <see cref="StreamMany{T}"/> (the single-arity version) for regular
/// <see cref="System.Linq.IQueryable{T}"/>-based queries.
/// </para>
/// </summary>
/// <typeparam name="TDoc">The Marten document type the query runs against.</typeparam>
/// <typeparam name="TOut">
/// The projected return type of the compiled query. Typically
/// <c>IEnumerable&lt;TItem&gt;</c> — e.g. when using
/// <see cref="ICompiledListQuery{TDoc, TItem}"/>.
/// </typeparam>
public sealed class StreamMany<TDoc, TOut> : IResult, IEndpointMetadataProvider where TDoc : notnull
{
    private readonly IQuerySession _session;
    private readonly ICompiledQuery<TDoc, TOut> _query;

    /// <summary>
    /// Create a <see cref="StreamMany{TDoc, TOut}"/> wrapping a compiled query.
    /// All matching results are streamed as a JSON array.
    /// </summary>
    public StreamMany(IQuerySession session, ICompiledQuery<TDoc, TOut> query)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _query = query ?? throw new ArgumentNullException(nameof(query));
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
        return _session.WriteArray(_query, httpContext, ContentType, OnFoundStatus);
    }

    /// <summary>
    /// Populates endpoint metadata so OpenAPI advertises a <c>200: TOut</c> response
    /// for this endpoint. No 404 is advertised because an empty array is a valid response.
    /// </summary>
    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));

        builder.Metadata.Add(new ProducesResponseTypeMetadata(
            StatusCodes.Status200OK, typeof(TOut), new[] { "application/json" }));
    }
}
