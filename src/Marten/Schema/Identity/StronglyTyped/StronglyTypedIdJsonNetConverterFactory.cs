using System;
using Newtonsoft.Json;

namespace Marten.Schema.Identity.StronglyTyped
{
    internal static class StronglyTypedIdJsonNetConverterFactory
    {
        public static JsonConverter Create(Type idType, Type primitiveType)
        {
            return (JsonConverter)Activator.CreateInstance(
                typeof(StronglyTypedIdJsonNetConverter<,>).MakeGenericType(idType, primitiveType));
        }
    }
}
