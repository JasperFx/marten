#nullable enable
using System;
using System.Collections.Generic;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Linq.QueryHandlers;

/// <summary>
/// Converts named-parameter SQL (using anonymous objects) to positional parameters
/// with proper NpgsqlDbType inference, so that null-valued parameters carry type
/// information to PostgreSQL. This fixes "42P08: could not determine data type of
/// parameter" errors when a named parameter's value is null.
/// </summary>
internal static class NamedParameterHelper
{
    /// <summary>
    /// Appends the SQL to the builder, replacing :paramName references with positional
    /// parameters that have proper NpgsqlDbType set based on the property's declared type.
    /// This ensures null parameter values still carry type information.
    /// </summary>
    public static void AppendSqlWithNamedParameters(ICommandBuilder builder, string sql, object parameters)
    {
        var properties = parameters.GetType().GetProperties();
        var propLookup = new Dictionary<string, (object? value, Type propertyType)>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in properties)
        {
            propLookup[property.Name] = (property.GetValue(parameters), property.PropertyType);
        }

        // Walk through the SQL, find :paramName references, replace with positional parameters
        var i = 0;
        while (i < sql.Length)
        {
            var c = sql[i];

            // Skip single-quoted strings
            if (c == '\'')
            {
                var end = sql.IndexOf('\'', i + 1);
                if (end == -1) end = sql.Length - 1;
                builder.Append(sql.Substring(i, end - i + 1));
                i = end + 1;
                continue;
            }

            // Check for :: (PostgreSQL type cast) - skip it
            if (c == ':' && i + 1 < sql.Length && sql[i + 1] == ':')
            {
                builder.Append("::");
                i += 2;
                continue;
            }

            // Check for :paramName
            if (c == ':' && i + 1 < sql.Length && IsIdentifierStart(sql[i + 1]))
            {
                var nameStart = i + 1;
                var nameEnd = nameStart;
                while (nameEnd < sql.Length && IsIdentifierChar(sql[nameEnd]))
                {
                    nameEnd++;
                }

                var paramName = sql.Substring(nameStart, nameEnd - nameStart);

                if (propLookup.TryGetValue(paramName, out var entry))
                {
                    var (value, propertyType) = entry;
                    var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

                    NpgsqlDbType? dbType = null;
                    if (value != null && PostgresqlProvider.Instance.HasTypeMapping(value.GetType()))
                    {
                        dbType = PostgresqlProvider.Instance.ToParameterType(value.GetType());
                    }
                    else if (PostgresqlProvider.Instance.HasTypeMapping(underlyingType))
                    {
                        dbType = PostgresqlProvider.Instance.ToParameterType(underlyingType);
                    }

                    builder.AppendParameter((object?)value ?? DBNull.Value, dbType);
                }
                else
                {
                    // Unknown parameter name - pass through as-is
                    builder.Append(sql.Substring(i, nameEnd - i));
                }

                i = nameEnd;
                continue;
            }

            builder.Append(c);
            i++;
        }
    }

    private static bool IsIdentifierStart(char c)
    {
        return char.IsLetter(c) || c == '_';
    }

    private static bool IsIdentifierChar(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_';
    }
}
