using System;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.PLv8.Transforms;

namespace Marten.PLv8
{
    public static class StoreOptionsExtensions
    {
        /// <summary>
        /// Add PLV8 related Patch() and Transform() operations to this Marten DocumentStore
        /// </summary>
        /// <param name="options"></param>
        /// <param name="configure">Optionally add custom JavaScript tranformations</param>
        public static void UseJavascriptTransformsAndPatching(this StoreOptions options, Action<ITransforms> configure = null)
        {
            var schema = new TransformSchema(options);
            configure?.Invoke(schema);
            options.Storage.Add(schema);
        }

        /// <summary>
        /// Synchronously apply one or more Javascript document transformations
        /// </summary>
        /// <param name="store"></param>
        /// <param name="apply"></param>
        public static void Transform(this IDocumentStore store, Action<IDocumentTransforms> apply)
        {
            var s = store.As<DocumentStore>();
            s.Tenancy.Default.EnsureStorageExists(typeof(TransformSchema));

            using var transforms = new DocumentTransforms(s, s.Tenancy.Default);
            apply(transforms);
            transforms.Session.SaveChanges();
        }

        /// <summary>
        /// Asynchronously apply one or more Javascript document transformations
        /// </summary>
        /// <param name="store"></param>
        /// <param name="apply"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public static async Task TransformAsync(this IDocumentStore store, Action<IDocumentTransforms> apply, CancellationToken token = default)
        {
            var s = store.As<DocumentStore>();
            s.Tenancy.Default.EnsureStorageExists(typeof(TransformSchema));
            using var transforms = new DocumentTransforms(s, s.Tenancy.Default);
            apply(transforms);
            await transforms.Session.SaveChangesAsync(token);
        }
    }
}
