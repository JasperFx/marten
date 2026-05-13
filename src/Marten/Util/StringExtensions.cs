#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Schema;

namespace Marten.Util;

internal static class StringExtensionMethods
{
    private static readonly ConcurrentDictionary<string, Regex> _removeTableAliasRegexCache = new();

    /// <summary>
    /// Process-wide list of optional member-name resolvers. Marten core leaves this empty
    /// and only honors <see cref="JsonPropertyNameAttribute"/> (STJ). The optional
    /// <c>Marten.Newtonsoft</c> package registers a Newtonsoft <c>JsonPropertyAttribute</c>
    /// resolver in its module initializer so member-name resolution under the Newtonsoft
    /// serializer continues to honor <c>[JsonProperty]</c> attributes the way pre-9.0
    /// behavior did.
    /// </summary>
    internal static readonly List<Func<MemberInfo, string?>> AdditionalMemberNameResolvers = new();

    public static string ToSnakeCase(this string s)
    {
        // 9.0: was Newtonsoft.Json.Serialization.SnakeCaseNamingStrategy. STJ's
        // JsonNamingPolicy.SnakeCaseLower (added in .NET 8) produces identical output
        // for ASCII identifiers — what Marten uses this for.
        return JsonNamingPolicy.SnakeCaseLower.ConvertName(s);
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

        // Run extra resolvers first (e.g. Newtonsoft's [JsonProperty]) so that the STJ
        // attribute below wins when both are present — preserves the pre-9.0 precedence
        // where STJ's [JsonPropertyName] overrode Newtonsoft's [JsonProperty].
        var extras = AdditionalMemberNameResolvers;
        for (var i = 0; i < extras.Count; i++)
        {
            var resolved = extras[i](member);
            if (resolved is not null) memberLocator = resolved;
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
