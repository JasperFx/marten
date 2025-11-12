#nullable enable
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Marten.Util;

internal static class StringExtensionMethods
{
    private static readonly SnakeCaseNamingStrategy _snakeCaseNamingStrategy = new();
    private static readonly ConcurrentDictionary<string, Regex> _removeTableAliasRegexCache = new();

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

    /// <summary>
    /// Remove table alias from a SQL string. This is also a candidate to move to Weasel.
    /// </summary>
    /// <param name="sql"></param>
    /// <param name="tableAlias"></param>
    /// <returns>SQL string</returns>
    public static string RemoveTableAlias(this string sql, string tableAlias)
    {
        if (string.IsNullOrEmpty(sql) || string.IsNullOrEmpty(tableAlias))
            return sql;

        // Remove 'd.' only when it's NOT followed by 'mt_' (anything followed bt mt_ will be schema name)
        var regex = _removeTableAliasRegexCache.GetOrAdd(tableAlias, alias =>
            new Regex(@$"\b{Regex.Escape(alias)}\.(?!mt_)", RegexOptions.Compiled));
        return regex.Replace(sql, "");
    }
}
