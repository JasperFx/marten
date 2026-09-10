#nullable enable
using System;
using System.Collections.Concurrent;

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
///     to the enum before the serializer is asked. The answer is cached per member.
///     </para>
///     <para>
///     Reflection over the enum's fields is the obvious alternative and it is a trap: it agrees under a
///     JIT and returns the declared name from a trimmed Native AOT binary, where the source-generated
///     converter has the renamed one baked in. <c>Marten.AotRuntimeSmoke</c> is where that is proven.
///     </para>
/// </remarks>
internal static class EnumMemberNames
{
    private static readonly ConcurrentDictionary<(Type, string), string> _names = new();

    public static string Of(ISerializer serializer, Type enumType, object value)
    {
        var member = Enum.ToObject(enumType, value);

        return _names.GetOrAdd((enumType, member.ToString()!),
            _ => serializer.ToCleanJson(member).Trim('"'));
    }
}
