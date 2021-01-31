using System;

namespace Marten.Services.Json
{
    internal static class SerializerFactory
    {
        public static SerializerType DefaultSerializerType { get; set; } = SerializerType.Newtonsoft;

        public static ISerializer New(SerializerType? serializerType = null, SerializerOptions options = null)
        {
            serializerType ??= DefaultSerializerType;

            options ??= new SerializerOptions();

            return serializerType switch
            {
                SerializerType.Newtonsoft => new JsonNetSerializer
                {
                    EnumStorage = options.EnumStorage,
                    Casing = options.Casing,
                    CollectionStorage = options.CollectionStorage,
                    NonPublicMembersStorage = options.NonPublicMembersStorage
                },
                SerializerType.SystemTextJson => new SystemTextJsonSerializer
                {
                    EnumStorage = options.EnumStorage, Casing = options.Casing
                },
                _ => throw new ArgumentOutOfRangeException(nameof(serializerType), serializerType, null)
            };
        }
    }
}
