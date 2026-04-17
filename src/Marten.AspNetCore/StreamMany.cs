using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;

namespace Marten.AspNetCore;

/// <summary>
/// Minimal-API / Wolverine.Http endpoint return value that streams a JSON array of
/// Marten documents directly to the <see cref="HttpContext.Response"/>. Uses
/// <see cref="QueryableExtensions.WriteArray{T}"/> under the hood — the JSON array is
/// written straight to the response stream without a deserialize/serialize round-trip.
/// <para>
/// Unlike <see cref="StreamOne{T}"/>, this type never returns 404: an empty result
/// set yields an empty JSON array (<c>[]</c>) with status <see cref="OnFoundStatus"/>
/// (default 200).
/// </para>
/// </summary>
/// <typeparam name="T">The document type contained in the array.</typeparam>
public sealed class StreamMany<T> : IResult, IEndpointMetadataProvider
{
    private readonly IQueryable<T> _queryable;

    /// <summary>
    /// Create a <see cref="StreamMany{T}"/> wrapping a Marten <see cref="IQueryable{T}"/>.
    /// All matching documents are streamed as a JSON array.
    /// </summary>
    public StreamMany(IQueryable<T> queryable)
    {
        _queryable = queryable ?? throw new ArgumentNullException(nameof(queryable));
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
        return _queryable.WriteArray(httpContext, ContentType, OnFoundStatus);
    }

    /// <summary>
    /// Populates endpoint metadata so OpenAPI correctly advertises a
    /// <c>200: T[]</c> response for this endpoint. No 404 is advertised
    /// because an empty array is a valid response.
    /// </summary>
    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));

        builder.Metadata.Add(new ProducesResponseTypeMetadata(
            StatusCodes.Status200OK, typeof(IReadOnlyList<T>), new[] { "application/json" }));
    }
}
