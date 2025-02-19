using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Internal.Sessions;
using Marten.PLv8.Transforms;
using Weasel.Postgresql;

namespace Marten.PLv8;

public static class StoreOptionsExtensions
{
    /// <summary>
    /// Add PLV8 related Patch() and Transform() operations to this Marten DocumentStore
    /// </summary>
    /// <param name="options"></param>
    /// <param name="configure">Optionally add custom JavaScript transformations</param>
    public static void UseJavascriptTransformsAndPatching(
        this StoreOptions options,
        Action<ITransforms> configure = null,
        bool createPlv8 = false
    )
    {
        var schema = new TransformSchema(options);
        configure?.Invoke(schema);
        options.Storage.Add(schema);

        if (createPlv8)
            options.Storage.ExtendedSchemaObjects.Add(new Extension("plv8"));
    }

    /// <summary>
    /// Asynchronously apply one or more Javascript document transformations
    /// </summary>
    /// <param name="store"></param>
    /// <param name="apply"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    public static async Task TransformAsync(this IDocumentStore store, Action<IDocumentTransforms> apply,
        CancellationToken token = default)
    {
        var s = store.As<DocumentStore>();
        if (!s.Options.Advanced.DefaultTenantUsageEnabled)
        {
            throw new DefaultTenantUsageDisabledException("Use the overload that takes a tenantId argument");
        }

        var tenant = s.Tenancy.Default;

        await tenant.Database.EnsureStorageExistsAsync(typeof(TransformSchema), token).ConfigureAwait(false);
        using var transforms = new DocumentTransforms(s, tenant);
        apply(transforms);
        await transforms.Session.SaveChangesAsync(token).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously apply one or more Javascript document transformations
    /// </summary>
    /// <param name="store"></param>
    /// <param name="apply"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    public static async Task TransformAsync(this IDocumentStore store, string tenantId,
        Action<IDocumentTransforms> apply, CancellationToken token = default)
    {
        var s = store.As<DocumentStore>();

        var tenant = await s.Tenancy.GetTenantAsync(store.Options.MaybeCorrectTenantId(tenantId)).ConfigureAwait(false);

        await tenant.Database.EnsureStorageExistsAsync(typeof(TransformSchema), token).ConfigureAwait(false);
        using var transforms = new DocumentTransforms(s, tenant);
        apply(transforms);
        await transforms.Session.SaveChangesAsync(token).ConfigureAwait(false);
    }
}
