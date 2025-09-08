using System;
using System.Text.Json;
using Newtonsoft.Json;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using NodaTime.Text;
using NodaTime.Utility;
using JsonSerializer = Newtonsoft.Json.JsonSerializer;

namespace Marten.NodaTimePlugin;

public class InstantJsonConverter
{
    public static readonly StjConverter Stj = new();
    public static readonly NewtonsoftConverter Newtonsoft = new();

    private static readonly InstantPattern InstantIsoPattern = InstantPattern.ExtendedIso;

    private static readonly OffsetDateTimePattern InstantOffsetPattern =
        OffsetDateTimePattern.CreateWithInvariantCulture("uuuu'-'MM'-'dd'T'HH':'mm':'ss.FFFFFFo<G>");

    public class StjConverter: NodaConverterBase<Instant>
    {
        protected override Instant ReadJsonImpl(ref Utf8JsonReader reader, JsonSerializerOptions options)
        {
            var text = reader.GetString()!;
            return ParseInstant(text);
        }

        protected override void WriteJsonImpl(Utf8JsonWriter writer, Instant value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(InstantIsoPattern.Format(value));
        }
    }

    public class NewtonsoftConverter: NodaTime.Serialization.JsonNet.NodaConverterBase<Instant>
    {
        protected override Instant ReadJsonImpl(JsonReader reader, JsonSerializer serializer)
        {
            if (reader.TokenType != JsonToken.String)
            {
                throw new InvalidNodaDataException(
                    $"Unexpected token parsing {nameof(Instant)}. Expected String, got {reader.TokenType}.");
            }

            var text = reader.Value!.ToString();
            return ParseInstant(text!);
        }

        protected override void WriteJsonImpl(JsonWriter writer, Instant value, JsonSerializer serializer)
        {
            writer.WriteValue(InstantIsoPattern.Format(value));
        }
    }

    private static Instant ParseInstant(string text)
    {
        var isoParseResult = InstantIsoPattern.Parse(text);
        if (isoParseResult.Success)
        {
            return isoParseResult.Value;
        }

        var offsetParseResult = InstantOffsetPattern.Parse(text);
        if (offsetParseResult.Success)
        {
            return offsetParseResult.Value.ToInstant();
        }

        throw new AggregateException(isoParseResult.Exception, offsetParseResult.Exception);
    }
}
