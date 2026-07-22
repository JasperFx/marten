using System;
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
    /// is read via a follow-up metadata query and written as a quoted <c>ETag</c> response header.
    /// If the incoming request's <c>If-None-Match</c> header matches that version, a <c>304 Not
    /// Modified</c> is written instead, with an empty body.
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
    ) where T : notnull
    {
        var stream = Marten.Internal.SharedMemoryStreamManager.GetStream();
        var found = await queryable.StreamJsonFirstOrDefault(stream, context.RequestAborted).ConfigureAwait(false);

        if (!found)
        {
            context.Response.StatusCode = 404;
            context.Response.ContentLength = 0;
            return;
        }

        if (emitETag)
        {
            // The queryable was built off a Marten session; walk back to it (internal access
            // granted via [InternalsVisibleTo] on Marten's assembly) so we can look up the
            // document's mt_version without asking the caller to plumb an IQuerySession through.
            IMartenSession session = ((IMartenLinqQueryable)queryable).Session;

            stream.Position = 0;
            var entity = session.Serializer.FromJson<T>(stream);

            var metadata = await ((IQuerySession)session)
                .MetadataForAsync(entity, context.RequestAborted)
                .ConfigureAwait(false);

            if (metadata != null)
            {
                var etag = ETagHelpers.Format(metadata.CurrentVersion);

                if (ETagHelpers.IfNoneMatchMatches(context, etag))
                {
                    context.Response.StatusCode = StatusCodes.Status304NotModified;
                    context.Response.Headers["ETag"] = etag;
                    context.Response.ContentLength = 0;
                    return;
                }

                context.Response.Headers["ETag"] = etag;
            }
        }

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

}
