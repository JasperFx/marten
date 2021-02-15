using System;
using Marten.Services.Json;

namespace Marten.Testing.Harness
{
    public class TestsSettings
    {
        private static SerializerType? serializerType;

        public static SerializerType SerializerType
        {
            get
            {
                if (serializerType.HasValue)
                    return serializerType.Value;

                var defaultSerializerEnv = Environment.GetEnvironmentVariable("DEFAULT_SERIALIZER");

                serializerType = Enum.TryParse(defaultSerializerEnv, out SerializerType parsedSerializerType)
                    ? parsedSerializerType
                    : SerializerType.Newtonsoft;

                return serializerType.Value;
            }
        }
    }
}
