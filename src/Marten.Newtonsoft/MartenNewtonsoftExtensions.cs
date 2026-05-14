#nullable enable
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using JasperFx.Core.Reflection;
using Marten.Services;
using Marten.Services.Json;
using Marten.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Weasel.Core;

namespace Marten.Newtonsoft;

/// <summary>
///     Configuration extensions for the Newtonsoft.Json integration in <see cref="Marten"/>.
///     Reference this package to make <c>JsonNetSerializer</c> available as a
///     <see cref="ISerializer"/> choice, restore the pre-9.0 <c>JObject</c> child-document
///     behavior on <see cref="StoreOptions"/>, and honor Newtonsoft's
///     <see cref="JsonPropertyAttribute"/> on document members.
/// </summary>
/// <remarks>
///     The package's <see cref="ModuleInitializerAttribute"/> registers the Newtonsoft factory
///     with <see cref="SerializerFactory"/> and adds the <see cref="JsonPropertyAttribute"/>
///     resolver to <see cref="StringExtensionMethods.AdditionalMemberNameResolvers"/> on
///     first reference, so calling <see cref="UseNewtonsoftForSerialization"/> only needs
///     to attach the per-store JObject registration.
/// </remarks>
public static class MartenNewtonsoftExtensions
{
    /// <summary>
    ///     Module initializer that wires up the process-wide hooks the Marten core relies on
    ///     to consume the Newtonsoft integration. Fires on first method JIT in this assembly,
    ///     which happens as soon as any type from <c>Marten.Newtonsoft</c> is referenced.
    /// </summary>
#pragma warning disable CA2255 // ModuleInitializer is intentional here — wires up the package's serializer factory + attribute resolver on assembly load.
    [ModuleInitializer]
    internal static void Initialize()
    {
        // Newtonsoft serializer factory — populated so SerializerFactory.New() can return
        // a JsonNetSerializer when SerializerType.Newtonsoft is requested (e.g. test code
        // setting DefaultSerializerType from the DEFAULT_SERIALIZER env var).
        SerializerFactory.NewtonsoftFactory = options => new JsonNetSerializer
        {
            EnumStorage = options.EnumStorage,
            Casing = options.Casing,
            CollectionStorage = options.CollectionStorage,
            NonPublicMembersStorage = options.NonPublicMembersStorage
        };

        // Honor Newtonsoft's [JsonProperty(PropertyName = "...")] for column-name resolution
        // in LINQ queries — restores the pre-9.0 behavior that used to live inline in
        // StringExtensionMethods.ToJsonKey.
        if (!StringExtensionMethods.AdditionalMemberNameResolvers.Contains(NewtonsoftJsonPropertyResolver))
        {
            StringExtensionMethods.AdditionalMemberNameResolvers.Add(NewtonsoftJsonPropertyResolver);
        }
    }
#pragma warning restore CA2255

    private static string? NewtonsoftJsonPropertyResolver(MemberInfo member)
    {
        return member.TryGetAttribute<JsonPropertyAttribute>(out var att) && att.PropertyName is not null
            ? att.PropertyName
            : null;
    }

    /// <summary>
    ///     Configure Marten to serialize document JSON using Newtonsoft.Json (Json.NET).
    /// </summary>
    /// <remarks>
    ///     Registers a configured <see cref="JsonNetSerializer"/> as the active serializer
    ///     and adds <see cref="JObject"/> to <see cref="StoreOptions"/>'s
    ///     child-document type set so user documents with <c>JObject</c> properties keep
    ///     the pre-9.0 child-document LINQ semantics.
    /// </remarks>
    /// <param name="options">The store options to configure.</param>
    /// <param name="enumStorage">Enum storage style.</param>
    /// <param name="casing">Casing style to be used in serialization.</param>
    /// <param name="collectionStorage">Allow to set collection storage as raw arrays (without explicit types).</param>
    /// <param name="nonPublicMembersStorage">Allow non public members to be used during deserialization.</param>
    /// <param name="configure">Optional callback to mutate the underlying <see cref="JsonSerializerSettings"/>.</param>
    public static void UseNewtonsoftForSerialization(
        this StoreOptions options,
        EnumStorage enumStorage = EnumStorage.AsInteger,
        Casing casing = Casing.Default,
        CollectionStorage collectionStorage = CollectionStorage.Default,
        NonPublicMembersStorage nonPublicMembersStorage = NonPublicMembersStorage.Default,
        Action<JsonSerializerSettings>? configure = null)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));

        var serializer = new JsonNetSerializer
        {
            EnumStorage = enumStorage,
            Casing = casing,
            CollectionStorage = collectionStorage,
            NonPublicMembersStorage = nonPublicMembersStorage
        };

        if (configure is not null)
            serializer.Configure(configure);

        options.Serializer(serializer);

        // Per-store child-document type registration. Done at extension-call time (not in
        // the module initializer) so users who reference Marten.Newtonsoft but don't
        // actually opt into Newtonsoft serialization on a given store don't have JObject
        // routed to ChildDocument for that store's LINQ member resolution.
        options.ChildDocumentTypes.Add(typeof(JObject));
    }
}
