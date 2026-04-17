using System;
using System.Reflection;
using System.Threading.Tasks;
using Marten.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;

namespace Marten.AspNetCore;

/// <summary>
/// Minimal-API / Wolverine.Http endpoint return value that streams the first matching
/// result of a Marten <see cref="ICompiledQuery{TDoc, TOut}"/> as raw JSON directly to
/// the <see cref="HttpContext.Response"/>. Uses
/// <see cref="QueryableExtensions.WriteOne{TDoc, TOut}"/> under the hood — the JSON is
/// written straight to the response stream without a deserialize/serialize round-trip.
/// <para>
/// Returns HTTP <c>404</c> if the query produces no result, <see cref="OnFoundStatus"/>
/// (default 200) if it does. <see cref="HttpResponse.ContentLength"/> and
/// <see cref="HttpResponse.ContentType"/> are set automatically.
/// </para>
/// <para>
/// This is the <see cref="ICompiledQuery{TDoc, TOut}"/> overload. Use
/// <see cref="StreamOne{T}"/> (the single-arity version) for regular
/// <see cref="System.Linq.IQueryable{T}"/>-based queries.
/// </para>
/// </summary>
/// <typeparam name="TDoc">The Marten document type the query runs against.</typeparam>
/// <typeparam name="TOut">The projected return type of the compiled query.</typeparam>
public sealed class StreamOne<TDoc, TOut> : IResult, IEndpointMetadataProvider where TDoc : notnull
{
    private readonly IQuerySession _session;
    private readonly ICompiledQuery<TDoc, TOut> _query;

    /// <summary>
    /// Create a <see cref="StreamOne{TDoc, TOut}"/> wrapping a compiled query.
    /// The query's single result is streamed as JSON; 404 if none.
    /// </summary>
    public StreamOne(IQuerySession session, ICompiledQuery<TDoc, TOut> query)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _query = query ?? throw new ArgumentNullException(nameof(query));
    }

    /// <summary>
    /// Status code written when the query produces a result. Defaults to 200.
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
        return _session.WriteOne(_query, httpContext, ContentType, OnFoundStatus);
    }

    /// <summary>
    /// Populates endpoint metadata so OpenAPI correctly advertises a
    /// <c>200: TOut</c> and <c>404</c> response for this endpoint.
    /// </summary>
    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));

        builder.Metadata.Add(new ProducesResponseTypeMetadata(
            StatusCodes.Status200OK, typeof(TOut), new[] { "application/json" }));
        builder.Metadata.Add(new ProducesResponseTypeMetadata(
            StatusCodes.Status404NotFound, typeof(void), Array.Empty<string>()));
    }
}
