#nullable enable
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Marten.Linq;

/// <summary>
///     Writes the jsonb payload of a containment or JSONPath filter: Marten's own dictionary of member
///     names to the values a query compared against.
/// </summary>
/// <remarks>
///     <para>
///     Handing that dictionary to the store's serializer is what #5374 was. Under Native AOT the resolver
///     is a source-generated <c>JsonSerializerContext</c> — what the AOT guide asks for — and it carries
///     the consumer's documents, not <c>object[]</c>, not the dictionary, and not whichever enum a filter
///     happens to compare. An ordinary <c>.Any(x =&gt; x.Member == value)</c> therefore threw before the
///     query was ever sent.
///     </para>
///     <para>
///     So the split is by ownership. Two shapes are <em>Marten's</em> and are written here because the
///     consumer's resolver has no reason to know them: the <c>Dictionary&lt;string, object&gt;</c> payload
///     itself, and the <c>object[]</c> a child-collection filter wraps it in. Everything else is a value
///     out of the consumer's document — including a <em>typed</em> nested dictionary such as the
///     <c>Dictionary&lt;TKey, TValue&gt;</c> that <c>DictionaryMember</c> builds for a dictionary-member
///     query — and goes back through the serializer, which both knows how it was written and has the type.
///     </para>
///     <para>
///     Every branch here has to emit what the serializer emitted, because a payload that differs by one
///     character silently matches nothing; <c>Bug_5374_containment_payload_json</c> asserts exactly that.
///     Delegating typed dictionaries is #5385: Newtonsoft's contract resolver sets
///     <c>ProcessDictionaryKeys = true</c> for CamelCase and SnakeCase, so it cases dictionary keys, and a
///     nested dictionary carries the caller's own key rather than one Marten already cased. Writing those
///     keys verbatim produced a payload that matched nothing under those two casings. The keys of the
///     payload's own shape are safe precisely because they are already <c>member.ToJsonKey(casing)</c>, so
///     re-casing them is idempotent. (System.Text.Json is unaffected either way: Marten never sets
///     <c>DictionaryKeyPolicy</c>.)
///     </para>
///     <para>
///     What this deliberately does <strong>not</strong> honour, because rendering the primitives here is
///     the whole point under AOT: a consumer-configured <c>NumberHandling</c>, a custom converter for one
///     of the scalar types written below, or a custom <c>JavaScriptEncoder</c>. The encoder cannot matter —
///     Postgres parses the payload into jsonb, where an escaped and an unescaped character are the same
///     character. The other two would, and a document relying on them should compare through a member
///     Marten hands to the serializer instead.
///     </para>
/// </remarks>
internal static class JsonbPayload
{
    public static string ToJson(ISerializer serializer, object? payload)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            write(writer, serializer, payload);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void write(Utf8JsonWriter writer, ISerializer serializer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                return;

            // Marten's own payload shape, including the nested one a child-collection filter builds.
            // The keys are already cased member names, so they are written as they stand.
            case Dictionary<string, object> payload:
                writer.WriteStartObject();
                foreach (var pair in payload)
                {
                    writer.WritePropertyName(pair.Key);
                    write(writer, serializer, pair.Value);
                }

                writer.WriteEndObject();
                return;

            // #5385: any other dictionary is a typed member off the consumer's document, whose keys the
            // serializer may case (Newtonsoft does) and whose key type it may have a converter for. It
            // owns the whole shape, and its resolver has the type because it is part of a document.
            case IDictionary:
                writer.WriteRawValue(serializer.ToCleanJson(value));
                return;

            case string text:
                writer.WriteStringValue(text);
                return;

            // #5385: before IEnumerable, which would otherwise write a byte array as a list of numbers
            // where both serializers write a base64 string.
            case byte[] bytes:
                writer.WriteBase64StringValue(bytes);
                return;

            case bool flag:
                writer.WriteBooleanValue(flag);
                return;

            case int number:
                writer.WriteNumberValue(number);
                return;

            case long number:
                writer.WriteNumberValue(number);
                return;

            case short number:
                writer.WriteNumberValue(number);
                return;

            case byte number:
                writer.WriteNumberValue(number);
                return;

            case sbyte number:
                writer.WriteNumberValue(number);
                return;

            case uint number:
                writer.WriteNumberValue(number);
                return;

            case ulong number:
                writer.WriteNumberValue(number);
                return;

            case ushort number:
                writer.WriteNumberValue(number);
                return;

            case decimal number:
                writer.WriteNumberValue(number);
                return;

            case double number:
                writer.WriteNumberValue(number);
                return;

            case float number:
                writer.WriteNumberValue(number);
                return;

            case Guid guid:
                writer.WriteStringValue(guid);
                return;

            case DateTimeOffset timestamp:
                writer.WriteStringValue(timestamp);
                return;

            case DateTime timestamp:
                writer.WriteStringValue(timestamp);
                return;

            // "O" is what System.Text.Json writes for a date; a time is not here because its converter
            // trims the fractional part and the serializer should keep owning that.
            case DateOnly date:
                writer.WriteStringValue(date.ToString("O", CultureInfo.InvariantCulture));
                return;

            // object[] is how a filter over a child collection wraps its dictionary, and a value
            // collection arrives as whatever list the query held.
            case IEnumerable enumerable:
                writer.WriteStartArray();
                foreach (var item in enumerable)
                {
                    write(writer, serializer, item);
                }

                writer.WriteEndArray();
                return;

            default:
                // An enum, a record, a value object, anything carrying its own converter: the serializer
                // is the only thing that knows how the document was written with it, and it is a type the
                // consumer's resolver has because it is part of a document. An enum in particular cannot
                // be rendered from its member name — [JsonStringEnumMemberName] renames it, and the
                // attribute is not there to be read from a trimmed binary.
                writer.WriteRawValue(serializer.ToCleanJson(value));
                return;
        }
    }
}
