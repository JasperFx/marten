using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Exceptions;
using Marten.Internal.Sessions;
using Marten.Linq;
using Marten.Services;
using Marten.Storage;

namespace Marten.PLv8.Transforms
{
    public static class TransformExtensions
    {
        internal static TransformFunction TransformFor(this ITenant tenant, string transformName)
        {
            try
            {
                var schema = tenant.FindFeature(typeof(TransformSchema));
                if (schema == null)
                {
                    throw new InvalidOperationException(
                        $"Attempting to use a PLV8/Javascript related feature, but the support is not active in this DocumentStore. Did you forget to call {nameof(StoreOptions)}.{nameof(StoreOptionsExtensions.UseJavascriptTransformsAndPatching)}()?");
                }

                return schema.As<TransformSchema>().For(transformName);
            }
            catch (InvalidDocumentException)
            {
                throw new InvalidOperationException(
                    $"Attempting to use a PLV8/Javascript related feature, but the support is not active in this DocumentStore. Did you forget to call {nameof(StoreOptions)}.{nameof(StoreOptionsExtensions.UseJavascriptTransformsAndPatching)}()?");
            }
        }

        internal static void EnsureTransformsExist(this IDocumentOperations operations)
        {
            try
            {
                operations.As<DocumentSessionBase>().Tenant.EnsureStorageExists(typeof(TransformSchema));
            }
            catch (InvalidDocumentException)
            {
                throw new InvalidOperationException(
                    $"Attempting to use a PLV8/Javascript related feature, but the support is not active in this DocumentStore. Did you forget to call {nameof(StoreOptions)}.{nameof(StoreOptionsExtensions.UseJavascriptTransformsAndPatching)}()?");
            }
        }

        /// <summary>
        /// Write the JSON for one document via a named Javascript transform name to the supplied Stream
        /// </summary>
        /// <param name="queryable"></param>
        /// <param name="transformName"></param>
        /// <param name="destination"></param>
        /// <param name="token"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static Task StreamOneTransformed<T>(this IQueryable<T> queryable, string transformName,
            Stream destination, CancellationToken token)
        {
            return queryable.As<IMartenLinqQueryable>().StreamOneTransformed(transformName, destination, token);
        }


        private static async Task StreamOneTransformed(this IMartenLinqQueryable martenQueryable, string transformName,
            Stream destination, CancellationToken token)
        {
            var builder = martenQueryable.BuildLinqHandler();

            var session = martenQueryable.Session;
            var tenant = session.Tenant;

            await tenant.EnsureStorageExistsAsync(typeof(TransformSchema), token);

            var transform = tenant.TransformFor(transformName);

            builder.CurrentStatement.SelectClause =
                new TransformSelectClause<string>(transform, builder.CurrentStatement.SelectClause);

            var statement = builder.TopStatement;
            statement.Current().Limit = 1;
            var command = statement.BuildCommand();

            await session.Database.StreamOne(command, destination, token);
        }

        /// <summary>
        /// Write many document via a named Javascript transform name to the supplied Stream as a JSON array
        /// </summary>
        /// <param name="queryable"></param>
        /// <param name="transformName"></param>
        /// <param name="destination"></param>
        /// <param name="token"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static Task StreamManyTransformed<T>(this IQueryable<T> queryable, string transformName,
            Stream destination, CancellationToken token = default)
        {
            return StreamManyTransformed(queryable.As<IMartenLinqQueryable>(), transformName, destination, token);
        }

        private static async Task StreamManyTransformed(this IMartenLinqQueryable martenQueryable, string transformName,
            Stream destination, CancellationToken token)
        {
            var builder = martenQueryable.BuildLinqHandler();

            var session = martenQueryable.Session;
            var tenant = session.Tenant;

            await tenant.EnsureStorageExistsAsync(typeof(TransformSchema), token);

            var transform = tenant.TransformFor(transformName);

            builder.CurrentStatement.SelectClause =
                new TransformSelectClause<string>(transform, builder.CurrentStatement.SelectClause);

            var statement = builder.TopStatement;
            var command = statement.BuildCommand();

            await session.Database.StreamMany(command, destination, token);
        }

        /// <summary>
        /// Fetch the JSON for a single document transformed by the named Javascript transform
        /// </summary>
        /// <param name="queryable"></param>
        /// <param name="transformName"></param>
        /// <param name="token"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static async Task<string> TransformOneToJson<T>(this IQueryable<T> queryable, string transformName,
            CancellationToken token = default)
        {
            var stream = new MemoryStream();
            await queryable.StreamOneTransformed(transformName, stream, token);
            stream.Position = 0;
            return await stream.ReadAllTextAsync();
        }

        /// <summary>
        /// Fetch the JSON array string for many document transformed by the named Javascript transform
        /// </summary>
        /// <param name="queryable"></param>
        /// <param name="transformName"></param>
        /// <param name="token"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static async Task<string> TransformManyToJson<T>(this IQueryable<T> queryable, string transformName,
            CancellationToken token = default)
        {
            var stream = new MemoryStream();
            await queryable.StreamManyTransformed(transformName, stream, token);
            stream.Position = 0;
            return await stream.ReadAllTextAsync();
        }

        /// <summary>
        /// Return a single document transformed to type T by the named Javascript transform
        /// </summary>
        /// <param name="queryable"></param>
        /// <param name="transformName"></param>
        /// <param name="token"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static async Task<T> TransformOneTo<T>(this IQueryable queryable, string transformName,
            CancellationToken token = default)
        {
            var stream = new MemoryStream();
            var martenQueryable = queryable.As<IMartenLinqQueryable>();

            await martenQueryable.StreamOneTransformed(transformName, stream, token);
            stream.Position = 0;

            return martenQueryable.Session.Serializer.FromJson<T>(stream);
        }

        /// <summary>
        /// Return many documents transformed to a list of type T by the named Javascript transform
        /// </summary>
        /// <param name="queryable"></param>
        /// <param name="transformName"></param>
        /// <param name="token"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static async Task<IReadOnlyList<T>> TransformManyTo<T>(this IQueryable queryable, string transformName,
            CancellationToken token = default)
        {
            var stream = new MemoryStream();
            var martenQueryable = queryable.As<IMartenLinqQueryable>();
            await martenQueryable.StreamManyTransformed(transformName, stream, token);

            stream.Position = 0;

            return martenQueryable.Session.Serializer.FromJson<T[]>(stream);
        }
    }
}
