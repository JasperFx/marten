using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Marten.AspNetCore
{
    public static class QueryableExtensions
    {
        /// <summary>
        /// Write the JSON contents of a single document response from the Linq query to the HttpContext response, with status code 200 if found or
        /// 404 if not found
        /// </summary>
        /// <param name="queryable"></param>
        /// <param name="context"></param>
        /// <param name="contentType"></param>
        /// <param name="onFoundStatus"></param>
        /// <typeparam name="T"></typeparam>
        public static async Task WriteSingle<T>(this IQueryable<T> queryable, HttpContext context, string contentType = "application/json", int onFoundStatus = 200)
        {
            var stream = new MemoryStream();
            var found = await queryable.StreamJsonFirstOrDefault(stream, context.RequestAborted);

            if (found)
            {
                context.Response.StatusCode = 200;
                context.Response.ContentLength = stream.Length;
                context.Response.ContentType = contentType;

                stream.Position = 0;
                await stream.CopyToAsync(context.Response.Body);
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
        /// <typeparam name="T"></typeparam>
        public static async Task WriteArray<T>(this IQueryable<T> queryable, HttpContext context,
            string contentType = "application/json")
        {
            var stream = new MemoryStream();
            await queryable.StreamJsonArray(stream, context.RequestAborted);

            context.Response.StatusCode = 200;
            context.Response.ContentLength = stream.Length;
            context.Response.ContentType = contentType;

            stream.Position = 0;
            await stream.CopyToAsync(context.Response.Body);
        }

        /// <summary>
        /// Quickly write the JSON for a document by Id to an HttpContext. Will also handle status code mechanics
        /// </summary>
        /// <param name="json"></param>
        /// <param name="id"></param>
        /// <param name="context"></param>
        /// <param name="contentType"></param>
        /// <typeparam name="T"></typeparam>
        public static async Task WriteById<T>(this IJsonLoader json, string id, HttpContext context, string contentType = "application/json") where T : class
        {
            var stream = new MemoryStream();
            var found = await json.StreamById<T>(id, stream);
            if (found)
            {
                context.Response.StatusCode = 200;
                context.Response.ContentLength = stream.Length;
                context.Response.ContentType = contentType;

                stream.Position = 0;
                await stream.CopyToAsync(context.Response.Body);
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
        /// <typeparam name="T"></typeparam>
        public static async Task WriteById<T>(this IJsonLoader json, Guid id, HttpContext context, string contentType = "application/json") where T : class
        {
            var stream = new MemoryStream();
            var found = await json.StreamById<T>(id, stream);
            if (found)
            {
                context.Response.StatusCode = 200;
                context.Response.ContentLength = stream.Length;
                context.Response.ContentType = contentType;

                stream.Position = 0;
                await stream.CopyToAsync(context.Response.Body);
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
        /// <typeparam name="T"></typeparam>
        public static async Task WriteById<T>(this IJsonLoader json, int id, HttpContext context, string contentType = "application/json") where T : class
        {
            var stream = new MemoryStream();
            var found = await json.StreamById<T>(id, stream);
            if (found)
            {
                context.Response.StatusCode = 200;
                context.Response.ContentLength = stream.Length;
                context.Response.ContentType = contentType;

                stream.Position = 0;
                await stream.CopyToAsync(context.Response.Body);
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
        /// <typeparam name="T"></typeparam>
        public static async Task WriteById<T>(this IJsonLoader json, long id, HttpContext context, string contentType = "application/json") where T : class
        {
            var stream = new MemoryStream();
            var found = await json.StreamById<T>(id, stream);
            if (found)
            {
                context.Response.StatusCode = 200;
                context.Response.ContentLength = stream.Length;
                context.Response.ContentType = contentType;

                stream.Position = 0;
                await stream.CopyToAsync(context.Response.Body);
            }
            else
            {
                context.Response.StatusCode = 404;
                context.Response.ContentLength = 0;
            }
        }

    }
}
