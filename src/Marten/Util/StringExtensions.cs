#nullable enable
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Schema;
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
    /// Remove table alias from a SQL string.
    /// </summary>
    /// <param name="sql"></param>
    /// <param name="tableAlias"></param>
    /// <returns>SQL string</returns>
    public static string RemoveTableAlias(this string sql, string tableAlias)
    {
        if (string.IsNullOrEmpty(sql) || string.IsNullOrEmpty(tableAlias))
            return sql;

        // First pass: remove '<table_alias>.' for any the Marten defined metadata columns
        string[] metadataColumns = [SchemaConstants.DocumentTypeColumn, SchemaConstants.LastModifiedColumn,
            SchemaConstants.DotNetTypeColumn, SchemaConstants.VersionColumn, SchemaConstants.CreatedAtColumn,
            SchemaConstants.DeletedColumn, SchemaConstants.DeletedAtColumn];
        var metadataColumnRegex = _removeTableAliasRegexCache.GetOrAdd(@$"\b{Regex.Escape(tableAlias)}\.({string.Join("|", metadataColumns)})\b", pattern =>
            new Regex(pattern, RegexOptions.Compiled));
        sql = metadataColumnRegex.Replace(sql, "$1");

        if (!sql.Contains($"{tableAlias}."))
            return sql;

        // Second pass: remove '<table_alias>.' only when it's NOT followed by 'mt_' (anything followed by mt_ could possibly be schema name for a Marten function)
        var regex = _removeTableAliasRegexCache.GetOrAdd(@$"\b{Regex.Escape(tableAlias)}\.(?!mt_)", pattern =>
            new Regex(pattern, RegexOptions.Compiled));

        return regex.Replace(sql, "");
    }
}
