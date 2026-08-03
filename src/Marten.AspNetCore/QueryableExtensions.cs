using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Internal;
using Marten.Linq;
using Microsoft.AspNetCore.Http;

namespace Marten.AspNetCore;

public static class QueryableExtensions
{
    /// <summary>
    /// Write the JSON contents of a single document response from the Linq query to the HttpContext response, with status code <paramref name="onFoundStatus"/> if found or
    /// 404 if not found.
    /// <para>
    /// When <paramref name="emitETag"/> is true (the default), the document's <c>mt_version</c>
    /// is read <b>inline with the document in the same single round trip</b> (piggy-backed onto the
    /// streaming query, analogous to the <c>count(*) OVER()</c> stats column) and written as a quoted
    /// <c>ETag</c> response header — a quoted GUID under Guid optimistic concurrency, or a quoted
    /// integer for numeric-revision documents (projection targets, <c>IRevisioned</c> types), where
    /// a <c>SingleStreamProjection</c> target's revision is the source stream's version. If the
    /// incoming request's <c>If-None-Match</c> header matches that value, a <c>304 Not Modified</c>
    /// is written instead, with an empty body — and because the version is read off the row before
    /// the payload, the document is never copied into the response buffer on that path. Document
    /// types with neither version nor revision metadata enabled (no <c>mt_version</c> column) emit
    /// no ETag.
    /// </para>
    /// </summary>
    /// <param name="queryable"></param>
    /// <param name="context"></param>
    /// <param name="contentType"></param>
    /// <param name="onFoundStatus">Defaults to 200</param>
    /// <param name="emitETag">Defaults to true. Set to false to skip the ETag/conditional-request support.</param>
    /// <typeparam name="T"></typeparam>
    public static async Task WriteSingle<T>(
        this IQueryable<T> queryable,
        HttpContext context,
        string contentType = "application/json",
        int onFoundStatus = 200,
        bool emitETag = true
    )
    {
        var stream = Marten.Internal.SharedMemoryStreamManager.GetStream();

        if (!emitETag)
        {
            var found = await queryable.StreamJsonFirstOrDefault(stream, context.RequestAborted).ConfigureAwait(false);
            if (!found)
            {
                context.Response.StatusCode = 404;
                context.Response.ContentLength = 0;
                return;
            }

            await writeBufferedBody(context, stream, contentType, onFoundStatus).ConfigureAwait(false);
            return;
        }

        // Fetch the document JSON and its mt_version in a single round trip. The version rides
        // on the same streaming query (see MartenLinqQueryProvider.StreamOneWithVersion), so there
        // is no follow-up MetadataForAsync query and no re-deserialization of the buffered JSON.
        // The predicate runs once the version is known and before the payload is copied, so a
        // cache hit skips buffering a body that would only be discarded.
        var result = await ((MartenLinqQueryable<T>)queryable)
            .StreamJsonFirstOrDefaultWithVersion(stream,
                (version, revision) => !matchesIfNoneMatch(context, formatETag(version, revision)),
                context.RequestAborted)
            .ConfigureAwait(false);

        if (!result.Found)
        {
            context.Response.StatusCode = 404;
            context.Response.ContentLength = 0;
            return;
        }

        var etag = formatETag(result.Version, result.Revision);
        if (etag != null)
        {
            context.Response.Headers["ETag"] = etag;
        }

        if (!result.BodyWritten)
        {
            context.Response.StatusCode = StatusCodes.Status304NotModified;
            context.Response.ContentLength = 0;
            return;
        }

        await writeBufferedBody(context, stream, contentType, onFoundStatus).ConfigureAwait(false);
    }

    /// <summary>
    /// Format whichever <c>mt_version</c> flavor came back as a quoted strong ETag, or null when the
    /// document type carries neither (no <c>mt_version</c> column) — in which case no ETag is emitted
    /// and no conditional request can match.
    /// </summary>
    private static string? formatETag(Guid? version, long? revision)
        => version.HasValue
            ? ETagHelpers.Format(version.Value)
            : revision.HasValue
                ? ETagHelpers.Format(revision.Value)
                : null;

    private static bool matchesIfNoneMatch(HttpContext context, string? etag)
        => etag != null && ETagHelpers.IfNoneMatchMatches(context, etag);

    private static async Task writeBufferedBody(HttpContext context, System.IO.Stream stream, string contentType,
        int onFoundStatus)
    {
        context.Response.StatusCode = onFoundStatus;
        context.Response.ContentLength = stream.Length;
        context.Response.ContentType = contentType;

        stream.Position = 0;
        await stream.CopyToAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>
    /// Write the JSON content of a Linq query as a JSON array to the HttpContext response
    /// </summary>
    /// <param name="queryable"></param>
    /// <param name="context"></param>
    /// <param name="contentType"></param>
    /// <param name="onFoundStatus">Defaults to 200</param>
    /// <typeparam name="T"></typeparam>
    public static async Task WriteArray<T>(
        this IQueryable<T> queryable,
        HttpContext context,
        string contentType = "application/json",
        int onFoundStatus = 200
    )
    {
        context.Response.StatusCode = onFoundStatus;
        context.Response.ContentType = contentType;

        await queryable.StreamJsonArray(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>
    /// Write a single "page" of results from the Linq query as a JSON envelope -- containing
    /// paging metadata (pageNumber, pageSize, totalItemCount, pageCount, hasNextPage,
    /// hasPreviousPage) and the matching documents ("items") -- to the HttpContext response.
    /// This is a single round-trip to the database: the total item count is retrieved via a
    /// <c>count(*) OVER()</c> window function in the same query used to fetch the page of documents.
    /// </summary>
    /// <param name="queryable"></param>
    /// <param name="context"></param>
    /// <param name="pageNumber">1-based page number</param>
    /// <param name="pageSize"></param>
    /// <param name="contentType"></param>
    /// <param name="onFoundStatus">Defaults to 200</param>
    /// <typeparam name="T"></typeparam>
    public static async Task WritePaged<T>(
        this IQueryable<T> queryable,
        HttpContext context,
        int pageNumber,
        int pageSize,
        string contentType = "application/json",
        int onFoundStatus = 200
    )
    {
        context.Response.StatusCode = onFoundStatus;
        context.Response.ContentType = contentType;

        await queryable.StreamPagedJsonArray(context.Response.Body, pageNumber, pageSize, context.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>
    /// Quickly write the JSON for a document by Id to an HttpContext. Will also handle status code mechanics
    /// </summary>
    /// <param name="json"></param>
    /// <param name="id"></param>
    /// <param name="context"></param>
    /// <param name="contentType"></param>
    /// <param name="onFoundStatus">Defaults to 200</param>
    /// <typeparam name="T"></typeparam>
    public static async Task WriteById<T>(
        this IJsonLoader json,
        object id,
        HttpContext context,
        string contentType = "application/json",
        int onFoundStatus = 200
    ) where T : class
    {
        var stream = Marten.Internal.SharedMemoryStreamManager.GetStream();
        var found = await json.StreamById<T>(id, stream, context.RequestAborted).ConfigureAwait(false);
        if (found)
        {
            context.Response.StatusCode = onFoundStatus;
            context.Response.ContentLength = stream.Length;
            context.Response.ContentType = contentType;

            stream.Position = 0;
            await stream.CopyToAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
        }
        else
        {
            context.Response.StatusCode = 404;
            context.Response.ContentLength = 0;
        }
    }

    /// <summary>
    /// Quickly write the JSON for a document by Id to an HttpContext. Will also handle status code mechanics
    /// </summary>
    /// <param name="json"></param>
    /// <param name="id"></param>
    /// <param name="context"></param>
    /// <param name="contentType"></param>
    /// <param name="onFoundStatus">Defaults to 200</param>
    /// <typeparam name="T"></typeparam>
    public static async Task WriteById<T>(
        this IJsonLoader json,
        string id,
        HttpContext context,
        string contentType = "application/json",
        int onFoundStatus = 200
    ) where T : class
    {
        var stream = Marten.Internal.SharedMemoryStreamManager.GetStream();
        var found = await json.StreamById<T>(id, stream, context.RequestAborted).ConfigureAwait(false);
        if (found)
        {
            context.Response.StatusCode = onFoundStatus;
            context.Response.ContentLength = stream.Length;
            context.Response.ContentType = contentType;

            stream.Position = 0;
            await stream.CopyToAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
        }
        else
        {
            context.Response.StatusCode = 404;
            context.Response.ContentLength = 0;
        }
    }

    /// <summary>
    /// Quickly write the JSON for a document by Id to an HttpContext. Will also handle status code mechanics
    /// </summary>
    /// <param name="json"></param>
    /// <param name="id"></param>
    /// <param name="context"></param>
    /// <param name="contentType"></param>
    /// <param name="onFoundStatus">Defaults to 200</param>
    /// <typeparam name="T"></typeparam>
    public static async Task WriteById<T>(
        this IJsonLoader json,
        Guid id,
        HttpContext context,
        string contentType = "application/json",
        int onFoundStatus = 200
    ) where T : class
    {
        var stream = Marten.Internal.SharedMemoryStreamManager.GetStream();
        var found = await json.StreamById<T>(id, stream, context.RequestAborted).ConfigureAwait(false);
        if (found)
        {
            context.Response.StatusCode = onFoundStatus;
            context.Response.ContentLength = stream.Length;
            context.Response.ContentType = contentType;

            stream.Position = 0;
            await stream.CopyToAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
        }
        else
        {
            context.Response.StatusCode = 404;
            context.Response.ContentLength = 0;
        }
    }

    /// <summary>
    /// Quickly write the JSON for a document by Id to an HttpContext. Will also handle status code mechanics
    /// </summary>
    /// <param name="json"></param>
    /// <param name="id"></param>
    /// <param name="context"></param>
    /// <param name="contentType"></param>
    /// <param name="onFoundStatus">Defaults to 200</param>
    /// <typeparam name="T"></typeparam>
    public static async Task WriteById<T>(
        this IJsonLoader json,
        int id,
        HttpContext context,
        string contentType = "application/json",
        int onFoundStatus = 200
    ) where T : class
    {
        var stream = Marten.Internal.SharedMemoryStreamManager.GetStream();
        var found = await json.StreamById<T>(id, stream, context.RequestAborted).ConfigureAwait(false);
        if (found)
        {
            context.Response.StatusCode = onFoundStatus;
            context.Response.ContentLength = stream.Length;
            context.Response.ContentType = contentType;

            stream.Position = 0;
            await stream.CopyToAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
        }
        else
        {
            context.Response.StatusCode = 404;
            context.Response.ContentLength = 0;
        }
    }

    /// <summary>
    /// Quickly write the JSON for a document by Id to an HttpContext. Will also handle status code mechanics
    /// </summary>
    /// <param name="json"></param>
    /// <param name="id"></param>
    /// <param name="context"></param>
    /// <param name="contentType"></param>
    /// <param name="onFoundStatus">Defaults to 200</param>
    /// <typeparam name="T"></typeparam>
    public static async Task WriteById<T>(
        this IJsonLoader json,
        long id,
        HttpContext context,
        string contentType = "application/json",
        int onFoundStatus = 200
    ) where T : class
    {
        var stream = Marten.Internal.SharedMemoryStreamManager.GetStream();
        var found = await json.StreamById<T>(id, stream, context.RequestAborted).ConfigureAwait(false);
        if (found)
        {
            context.Response.StatusCode = onFoundStatus;
            context.Response.ContentLength = stream.Length;
            context.Response.ContentType = contentType;

            stream.Position = 0;
            await stream.CopyToAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
        }
        else
        {
            context.Response.StatusCode = 404;
            context.Response.ContentLength = 0;
        }
    }

    /// <summary>
    /// Write a single document returned from a compiled query to the
    /// given HttpContext
    /// </summary>
    /// <param name="session"></param>
    /// <param name="query"></param>
    /// <param name="context"></param>
    /// <param name="contentType"></param>
    /// <param name="onFoundStatus">Defaults to 200</param>
    /// <typeparam name="TDoc"></typeparam>
    /// <typeparam name="TOut"></typeparam>
    public static async Task WriteOne<TDoc, TOut>(
        this IQuerySession session,
        ICompiledQuery<TDoc, TOut> query,
        HttpContext context,
        string contentType = "application/json",
        int onFoundStatus = 200
        ) where TDoc : notnull
    {
        var stream = Marten.Internal.SharedMemoryStreamManager.GetStream();
        var found = await session.StreamJsonOne(query, stream, context.RequestAborted).ConfigureAwait(false);
        if (found)
        {
            context.Response.StatusCode = onFoundStatus;
            context.Response.ContentLength = stream.Length;
            context.Response.ContentType = contentType;

            stream.Position = 0;
            await stream.CopyToAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
        }
        else
        {
            context.Response.StatusCode = 404;
            context.Response.ContentLength = 0;
        }
    }

    /// <summary>
    /// Write an array of documents as a JSON array from a compiled query
    /// to the HttpContext
    /// </summary>
    /// <param name="session"></param>
    /// <param name="query"></param>
    /// <param name="context"></param>
    /// <param name="contentType"></param>
    /// <param name="onFoundStatus">Defaults to 200</param>
    /// <typeparam name="TDoc"></typeparam>
    /// <typeparam name="TOut"></typeparam>
    public static async Task WriteArray<TDoc, TOut>(
        this IQuerySession session,
        ICompiledQuery<TDoc, TOut> query,
        HttpContext context,
        string contentType = "application/json",
        int onFoundStatus = 200
        ) where TDoc : notnull
    {
        context.Response.StatusCode = onFoundStatus;
        context.Response.ContentType = contentType;

        await session.StreamJsonMany(query, context.Response.Body, context.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>
    /// Write an raw SQL query result directly to the HttpContext
    /// </summary>
    /// <param name="session"></param>
    /// <param name="sql"></param>
    /// <param name="context"></param>
    /// <param name="contentType"></param>
    /// <param name="onFoundStatus">Defaults to 200</param>
    public static async Task WriteJson(
        this IQuerySession session,
        string sql,
        HttpContext context,
        string contentType = "application/json",
        int onFoundStatus = 200,
        params object[] parameters
    )
    {
        context.Response.StatusCode = onFoundStatus;
        context.Response.ContentType = contentType;

        await session.StreamJson<int>(context.Response.Body, context.RequestAborted, sql, parameters).ConfigureAwait(false);
    }

    /// <summary>
    /// Write the raw JSON of the latest projected aggregate T by Guid id directly to the HttpContext response,
    /// avoiding deserialization/serialization round-trips when possible. Returns 404 if not found.
    /// </summary>
    /// <param name="events"></param>
    /// <param name="id"></param>
    /// <param name="context"></param>
    /// <param name="contentType"></param>
    /// <param name="onFoundStatus">Defaults to 200</param>
    /// <typeparam name="T"></typeparam>
    public static async Task WriteLatest<T>(
        this IEventStoreOperations events,
        Guid id,
        HttpContext context,
        string contentType = "application/json",
        int onFoundStatus = 200
    ) where T : class
    {
        var stream = Marten.Internal.SharedMemoryStreamManager.GetStream();
        var found = await events.StreamLatestJson<T>(id, stream, context.RequestAborted).ConfigureAwait(false);
        if (found)
        {
            context.Response.StatusCode = onFoundStatus;
            context.Response.ContentLength = stream.Length;
            context.Response.ContentType = contentType;

            stream.Position = 0;
            await stream.CopyToAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
        }
        else
        {
            context.Response.StatusCode = 404;
            context.Response.ContentLength = 0;
        }
    }

    /// <summary>
    /// Write the raw JSON of the latest projected aggregate T by string id directly to the HttpContext response,
    /// avoiding deserialization/serialization round-trips when possible. Returns 404 if not found.
    /// </summary>
    /// <param name="events"></param>
    /// <param name="id"></param>
    /// <param name="context"></param>
    /// <param name="contentType"></param>
    /// <param name="onFoundStatus">Defaults to 200</param>
    /// <typeparam name="T"></typeparam>
    public static async Task WriteLatest<T>(
        this IEventStoreOperations events,
        string id,
        HttpContext context,
        string contentType = "application/json",
        int onFoundStatus = 200
    ) where T : class
    {
        var stream = Marten.Internal.SharedMemoryStreamManager.GetStream();
        var found = await events.StreamLatestJson<T>(id, stream, context.RequestAborted).ConfigureAwait(false);
        if (found)
        {
            context.Response.StatusCode = onFoundStatus;
            context.Response.ContentLength = stream.Length;
            context.Response.ContentType = contentType;

            stream.Position = 0;
            await stream.CopyToAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
        }
        else
        {
            context.Response.StatusCode = 404;
            context.Response.ContentLength = 0;
        }
    }

    /// <summary>
    /// Resolve a <see cref="FetchStreamStatePlan"/> and write the resulting stream metadata to the
    /// HttpContext response as JSON, or 404 when the stream does not exist.
    /// <para>
    /// The response body is a <see cref="StreamStateResponse"/> rather than Marten's
    /// <c>StreamState</c> — see that type for why.
    /// </para>
    /// </summary>
    /// <param name="session"></param>
    /// <param name="plan"></param>
    /// <param name="context"></param>
    /// <param name="contentType"></param>
    /// <param name="onFoundStatus">Defaults to 200</param>
    public static async Task WriteStreamState(
        this IQuerySession session,
        FetchStreamStatePlan plan,
        HttpContext context,
        string contentType = "application/json",
        int onFoundStatus = 200
    )
    {
        if (session == null) throw new ArgumentNullException(nameof(session));
        if (plan == null) throw new ArgumentNullException(nameof(plan));
        if (context == null) throw new ArgumentNullException(nameof(context));

        var state = await plan.Fetch(session, context.RequestAborted).ConfigureAwait(false);

        if (state == null)
        {
            context.Response.StatusCode = 404;
            context.Response.ContentLength = 0;
            return;
        }

        await writeJson(session, StreamStateResponse.From(state), context, contentType, onFoundStatus)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Resolve a <see cref="FetchStreamPlan"/> and write the resulting raw events to the HttpContext
    /// response as a JSON array.
    /// <para>
    /// Marten's <c>FetchStream</c> yields an empty list both for a stream that does not exist and for
    /// a filter that excludes every event, so the two cases cannot be told apart here.
    /// <paramref name="onEmptyStatus"/> decides which answer the endpoint gives; it defaults to 404
    /// to match the other single-resource results. Pass 200 to return an empty JSON array instead.
    /// </para>
    /// <para>
    /// Each element is an <see cref="EventResponse"/> rather than Marten's <c>IEvent</c> — see that
    /// type for why.
    /// </para>
    /// </summary>
    /// <param name="session"></param>
    /// <param name="plan"></param>
    /// <param name="context"></param>
    /// <param name="contentType"></param>
    /// <param name="onFoundStatus">Defaults to 200</param>
    /// <param name="onEmptyStatus">Defaults to 404</param>
    public static async Task WriteEvents(
        this IQuerySession session,
        FetchStreamPlan plan,
        HttpContext context,
        string contentType = "application/json",
        int onFoundStatus = 200,
        int onEmptyStatus = 404
    )
    {
        if (session == null) throw new ArgumentNullException(nameof(session));
        if (plan == null) throw new ArgumentNullException(nameof(plan));
        if (context == null) throw new ArgumentNullException(nameof(context));

        var events = await plan.Fetch(session, context.RequestAborted).ConfigureAwait(false);

        if (events.Count == 0 && onEmptyStatus == 404)
        {
            context.Response.StatusCode = 404;
            context.Response.ContentLength = 0;
            return;
        }

        await writeJson(session, EventResponse.From(events), context, contentType,
            events.Count == 0 ? onEmptyStatus : onFoundStatus).ConfigureAwait(false);
    }

    /// <summary>
    /// Serialize <paramref name="value"/> with the store's configured serializer and write it to the
    /// response, setting Content-Length. Buffers through a pooled <see cref="ArrayBufferWriter{T}"/>
    /// so the JSON never round-trips through a .NET string.
    /// </summary>
    private static async Task writeJson(
        IQuerySession session,
        object value,
        HttpContext context,
        string contentType,
        int statusCode)
    {
        var buffer = new ArrayBufferWriter<byte>();
        session.DocumentStore.Options.Serializer().WriteTo(buffer, value);

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = contentType;
        context.Response.ContentLength = buffer.WrittenCount;

        await context.Response.Body.WriteAsync(buffer.WrittenMemory, context.RequestAborted)
            .ConfigureAwait(false);
    }
}
