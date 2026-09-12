#nullable enable
using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Marten.Linq.Members;

/// <summary>
///     What the serializer calls an enum member when it stores it as a string.
/// </summary>
/// <remarks>
///     <para>
///     A query used to render the value with <c>Enum.GetName</c>, which is the member's declared name.
///     That is the stored name only until somebody renames the member: System.Text.Json honours
///     <c>[JsonStringEnumMemberName]</c> and Newtonsoft honours <c>[EnumMember]</c>, and a filter
///     comparing a renamed member against its declared name matches nothing and reports it as no rows
///     (#5376).
///     </para>
///     <para>
///     The value can arrive as the enum or as its underlying integer — the C# compiler emits
///     <c>Convert(x.Member, Int32) == 1</c> for a comparison against a literal — so it is converted back
///     to the enum before the serializer is asked.
///     </para>
///     <para>
///     Reflection over the enum's fields is the obvious alternative and it is a trap: it agrees under a
///     JIT and returns the declared name from a trimmed Native AOT binary, where the source-generated
///     converter has the renamed one baked in. <c>Marten.AotRuntimeSmoke</c> is where that is proven.
///     </para>
///     <para>
///     The answer is cached <em>per serializer</em>, not per enum type. Two stores in one process can
///     legitimately spell the same member differently — an ancillary store registered with
///     <c>AddMartenStore&lt;T&gt;()</c>, or one whose <c>Configure(...)</c> adds a
///     <c>JsonStringEnumConverter(JsonNamingPolicy.CamelCase)</c> where another does not — and a cache
///     keyed only by the type would serve whichever store asked first to both of them. That failure is
///     silent, and it is the same "no rows" shape this class exists to remove.
///     </para>
/// </remarks>
internal static class EnumMemberNames
{
    private static readonly ConditionalWeakTable<ISerializer, ConcurrentDictionary<(Type, string), string>>
        _bySerializer = new();

    /// <summary>
    ///     The name <paramref name="serializer" /> stores for <paramref name="value" />, which may be
    ///     passed as the enum itself or as its underlying integer.
    /// </summary>
    public static string Of(ISerializer serializer, Type enumType, object value)
    {
        var member = Enum.ToObject(enumType, value);
        var cache = _bySerializer.GetOrCreateValue(serializer);

        return cache.GetOrAdd((enumType, member.ToString()!), _ => Decode(serializer.ToCleanJson(member)));
    }

    /// <summary>
    ///     A renderer for the Weasel <c>EnumIsOneOf</c> / <c>EnumIsNotOneOf</c> fragments, which build a
    ///     string[] for an <c>in</c> filter over an enum member and otherwise fall back to the declared
    ///     name (weasel#591).
    /// </summary>
    public static Func<object, string> RendererFor(ISerializer serializer, Type enumType)
        => value => Of(serializer, enumType, value);

    /// <summary>
    ///     <c>ToCleanJson</c> hands back a JSON string literal, so the quotes come off — and so does any
    ///     escaping. System.Text.Json's default encoder escapes <c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c>,
    ///     <c>'</c> and <c>+</c>, so a member renamed to something containing one of those would other-
    ///     wise be compared as <c>a&b</c> against the <c>a&amp;b</c> that <c>-&gt;&gt;</c> returns:
    ///     no rows, silently, which is the very bug being fixed. Read through a
    ///     <see cref="Utf8JsonReader" /> rather than a deserialize so this stays AOT-safe and needs no
    ///     resolver.
    /// </summary>
    private static string Decode(string json)
    {
        // AsInteger never reaches here, but a non-string payload must not throw if it ever does.
        if (json.Length < 2 || json[0] != '"')
        {
            return json;
        }

        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        return reader.Read() && reader.TokenType == JsonTokenType.String
            ? reader.GetString()!
            : json.Trim('"');
    }
}
