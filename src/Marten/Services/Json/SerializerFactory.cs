#nullable enable
using System;

namespace Marten.Services.Json;

/// <summary>
///     Resolves the default <see cref="ISerializer"/> for new <see cref="StoreOptions"/> instances.
///     The Newtonsoft path is delegated to a factory registered by the optional
///     <c>Marten.Newtonsoft</c> package — referencing it triggers the module initializer that
///     populates <see cref="NewtonsoftFactory"/>. Without that package, only
///     <see cref="SerializerType.SystemTextJson"/> is supported.
/// </summary>
internal static class SerializerFactory
{
    public static SerializerType DefaultSerializerType { get; set; } = SerializerType.SystemTextJson;

    /// <summary>
    ///     Factory delegate for constructing a Newtonsoft-backed <see cref="ISerializer"/>.
    ///     Set by <c>Marten.Newtonsoft</c>'s module initializer when the package is referenced.
    ///     Left null in Marten core — calling <see cref="New"/> with
    ///     <see cref="SerializerType.Newtonsoft"/> when no factory is registered throws.
    /// </summary>
    public static Func<SerializerOptions, ISerializer>? NewtonsoftFactory { get; set; }

    public static ISerializer New(SerializerType? serializerType = null, SerializerOptions? options = null)
    {
        serializerType ??= DefaultSerializerType;

        options ??= new SerializerOptions();

        return serializerType switch
        {
            SerializerType.Newtonsoft => NewtonsoftFactory?.Invoke(options)
                ?? throw new InvalidOperationException(
                    "Newtonsoft serialization was requested but the Marten.Newtonsoft package is not loaded. " +
                    "Add the Marten.Newtonsoft NuGet package reference and call " +
                    "StoreOptions.UseNewtonsoftForSerialization() (extension method in the Marten.Newtonsoft namespace)."),
            SerializerType.SystemTextJson => new SystemTextJsonSerializer
            {
                EnumStorage = options.EnumStorage, Casing = options.Casing
            },
            _ => throw new ArgumentOutOfRangeException(nameof(serializerType), serializerType, null)
        };
    }
}
