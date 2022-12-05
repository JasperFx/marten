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

    public static string FormatCase(this string s, Casing casing)
    {
        switch (casing)
        {
            case Casing.CamelCase:
                return s.ToCamelCase();

            case Casing.SnakeCase:
                return s.ToSnakeCase();

            default:
                return s;
        }
    }
}
