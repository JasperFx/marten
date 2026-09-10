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
///     query was ever sent. The shape is ours, so we write it; only a value we cannot render identically
///     goes through the serializer, and that is a document type the consumer has already declared.
///     </para>
///     <para>
///     Every branch here has to emit what the serializer emitted, and <c>Bug_5374_containment_payload_json</c>
///     asserts exactly that, because a payload that differs by one character silently matches nothing.
///     Two properties of the serializer's options matter and both are honoured: the naming policy is a
///     <em>property</em> policy and Marten never sets <c>DictionaryKeyPolicy</c>, so these keys are written
///     verbatim, and any value with a converter of its own — an enum included — goes back through the
///     serializer. A custom
///     <c>JavaScriptEncoder</c> is the one thing not carried across, and it cannot matter: Postgres parses
///     the payload into jsonb, where an escaped and an unescaped character are the same character.
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

            // The payload's own shape, and a nested Dictionary<TKey, TValue> where a query filtered a
            // dictionary member.
            case IDictionary dictionary:
                writer.WriteStartObject();
                foreach (DictionaryEntry entry in dictionary)
                {
                    writer.WritePropertyName(key(entry.Key));
                    write(writer, serializer, entry.Value);
                }

                writer.WriteEndObject();
                return;

            case string text:
                writer.WriteStringValue(text);
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

    /// <summary>A dictionary key is a JSON property name, and System.Text.Json renders a non-string key
    /// as its invariant text. An enum key is its member name whatever <see cref="ISerializer.EnumStorage" />
    /// says — that setting is about values, and a key has its own converter.</summary>
    private static string key(object value) => value switch
    {
        string text => text,
        Enum => value.ToString()!,
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
    };
}
