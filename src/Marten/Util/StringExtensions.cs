#nullable enable
using JasperFx.Core;
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
}
