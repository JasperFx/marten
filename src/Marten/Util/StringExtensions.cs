#nullable enable
using System.Reflection;
using System.Text.Json.Serialization;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Marten.Util;

internal static class StringExtensionMethods
{
    private static readonly SnakeCaseNamingStrategy _snakeCaseNamingStrategy = new();

    public static string ToSnakeCase(this string s)
    {
        return _snakeCaseNamingStrategy.GetPropertyName(s, false);
    }

    public static string FormatCase(this string s, Casing casing) =>
        casing switch
        {
            Casing.CamelCase => s.ToCamelCase(),
            Casing.SnakeCase => s.ToSnakeCase(),
            _ => s
        };

    public static string ToJsonKey(this MemberInfo member, Casing casing)
    {
        var memberLocator = member.Name.FormatCase(casing);
        if (member.TryGetAttribute<JsonPropertyAttribute>(out var newtonsoftAtt) && newtonsoftAtt.PropertyName is not null)
        {
            memberLocator = newtonsoftAtt.PropertyName;
        }

        if (member.TryGetAttribute<JsonPropertyNameAttribute>(out var stjAtt))
        {
            memberLocator = stjAtt.Name;
        }

        return memberLocator;
    }
}
