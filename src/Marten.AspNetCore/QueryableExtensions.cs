using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Marten.Linq;
using Microsoft.AspNetCore.Http;

namespace Marten.AspNetCore;

public static class QueryableExtensions
{
    /// <summary>
    /// Write the JSON contents of a single document response from the Linq query to the HttpContext response, with status code <paramref name="onFoundStatus"/> if found or
    /// 404 if not found
    /// </summary>
    /// <param name="queryable"></param>
    /// <param name="context"></param>
    /// <param name="contentType"></param>
    /// <param name="onFoundStatus">Defaults to 200</param>
    /// <typeparam name="T"></typeparam>
    public static async Task WriteSingle<T>(
        this IQueryable<T> queryable,
        HttpContext context,
        string contentType = "application/json",
        int onFoundStatus = 200
    )
    {
        var stream = new MemoryStream();
        var found = await queryable.StreamJsonFirstOrDefault(stream, context.RequestAborted).ConfigureAwait(false);

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
        var stream = new MemoryStream();
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
        var stream = new MemoryStream();
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
        var stream = new MemoryStream();
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
        var stream = new MemoryStream();
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
        var stream = new MemoryStream();
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
        )
    {
        var stream = new MemoryStream();
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
        )
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

}
