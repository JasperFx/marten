using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NodaTime;
using NodaTime.Serialization.JsonNet;
using NodaTime.Text;

namespace Marten.NodaTime
{
    /// <summary>
    /// Class used to properly deserialize Instants when db won't return data in TZ format.
    /// The need come up with https://github.com/JasperFx/marten/issues/1276.
    /// This is workaround for NodaTime issues https://github.com/nodatime/nodatime/issues/154 and https://github.com/nodatime/nodatime/pull/1237.
    /// Revisit this when NodaTime 3.0.0 will be officialy released - then this class might be not needed anymore
    /// </summary>
    internal class InstantJsonConverter: JsonConverter
    {
        public static InstantJsonConverter Instance = new InstantJsonConverter();

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            NodaConverters.InstantConverter.WriteJson(writer, value, serializer);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return NodaConverters.InstantConverter.ReadJson(reader, objectType, existingValue, serializer);

            var token = JToken.Load(reader);
            var stringValue = token.ToObject<string>();
            var parseResult = InstantPattern.ExtendedIso.Parse(stringValue);

            if (parseResult.Success)
                return parseResult.Value;

            return Instant.FromDateTimeUtc(DateTime.SpecifyKind(DateTime.Parse(stringValue), DateTimeKind.Utc));
        }

        public override bool CanConvert(Type objectType)
        {
            return typeof(Instant) == objectType;
        }
    }
}
