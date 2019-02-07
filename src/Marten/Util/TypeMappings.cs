using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Baseline;
using Npgsql;
using Npgsql.TypeMapping;
using NpgsqlTypes;

namespace Marten.Util
{
    public static class TypeMappings
    {
        private static readonly Ref<ImHashMap<Type, string>> PgTypeMemo;
        private static readonly Ref<ImHashMap<Type, NpgsqlDbType?>> NpgsqlDbTypeMemo;

        static TypeMappings()
        {
            // Initialize PgTypeMemo with Types which are not available in Npgsql mappings
            PgTypeMemo = Ref.Of(ImHashMap<Type, string>.Empty);

            PgTypeMemo.Swap(d => d.AddOrUpdate(typeof(long), "bigint"));
            PgTypeMemo.Swap(d => d.AddOrUpdate(typeof(string), "varchar"));
            PgTypeMemo.Swap(d => d.AddOrUpdate(typeof(float), "decimal"));

            // Default Npgsql mapping is 'numeric' but we are using 'decimal'
            PgTypeMemo.Swap(d => d.AddOrUpdate(typeof(decimal), "decimal"));

            // Default Npgsql mappings is 'timestamp' but we are using 'timestamp without time zone'
            PgTypeMemo.Swap(d => d.AddOrUpdate(typeof(DateTime), "timestamp without time zone"));

            NpgsqlDbTypeMemo = Ref.Of(ImHashMap<Type, NpgsqlDbType?>.Empty);
        }

        // Lazily retrieve the CLR type to NpgsqlDbType and PgTypeName mapping from exposed INpgsqlTypeMapper.Mappings.
        // This is lazily calculated instead of precached because it allows consuming code to register
        // custom npgsql mappings prior to execution.
        private static string ResolvePgType(Type type)
        {
            if (PgTypeMemo.Value.TryFind(type, out var value)) return value;

            value = GetTypeMapping(type)?.PgTypeName;

            PgTypeMemo.Swap(d => d.AddOrUpdate(type, value));

            return value;
        }

        private static NpgsqlDbType? ResolveNpgsqlDbType(Type type)
        {
            if (NpgsqlDbTypeMemo.Value.TryFind(type, out var value)) return value;

            value = GetTypeMapping(type)?.NpgsqlDbType;

            NpgsqlDbTypeMemo.Swap(d => d.AddOrUpdate(type, value));

            return value;
        }

        private static NpgsqlTypeMapping GetTypeMapping(Type type)
            => NpgsqlConnection
                .GlobalTypeMapper
                .Mappings
                .FirstOrDefault(mapping => mapping.ClrTypes.Contains(type));

        public static string ConvertSynonyms(string type)
        {
            switch (type.ToLower())
            {
                case "character varying":
                case "varchar":
                    return "varchar";

                case "boolean":
                case "bool":
                    return "boolean";

                case "integer":
                    return "int";

                case "integer[]":
                    return "int[]";

                case "decimal":
                case "numeric":
                    return "decimal";

                case "timestamp without time zone":
                    return "timestamp";

                case "timestamp with time zone":
                    return "timestamptz";
            }

            return type;
        }

        public static string ReplaceMultiSpace(this string str, string newStr)
        {
            var regex = new Regex("\\s+");
            return regex.Replace(str, newStr);
        }

        public static string CanonicizeSql(this string sql)
        {
            var replaced = sql
                .Trim()
                .Replace('\n', ' ')
                .Replace('\r', ' ')
                .Replace('\t', ' ')
                .ReplaceMultiSpace(" ")
                .Replace(" ;", ";")
                .Replace("SECURITY INVOKER", "")
                .Replace("  ", " ")
                .Replace("LANGUAGE plpgsql AS $function$", "")
                .Replace("$$ LANGUAGE plpgsql", "$function$")
                .Replace("AS $$ DECLARE", "DECLARE")
                .Replace("character varying", "varchar")
                .Replace("Boolean", "boolean")
                .Replace("bool,", "boolean,")
                .Replace("int[]", "integer[]")
                .Replace("numeric", "decimal").TrimEnd(';').TrimEnd();

            if (replaced.Contains("PLV8", StringComparison.OrdinalIgnoreCase))
            {
                replaced = replaced
                    .Replace("LANGUAGE plv8 IMMUTABLE STRICT AS $function$", "AS $$");

                const string languagePlv8ImmutableStrict = "$$ LANGUAGE plv8 IMMUTABLE STRICT";
                const string functionMarker = "$function$";
                if (replaced.EndsWith(functionMarker))
                {
                    replaced = replaced.Substring(0, replaced.LastIndexOf(functionMarker)) + languagePlv8ImmutableStrict;
                }
            }

            return replaced
                .Replace("  ", " ").TrimEnd().TrimEnd(';');
        }

        /// <summary>
        /// Some portion of implementation adapted from Npgsql GlobalTypeMapper.ToNpgsqlDbType(Type type)
        /// https://github.com/npgsql/npgsql/blob/dev/src/Npgsql/TypeMapping/GlobalTypeMapper.cs
        /// Possibly this method can be trimmed down when Npgsql eventually exposes ToNpgsqlDbType
        /// </summary>
        public static NpgsqlDbType ToDbType(Type type)
        {
            var npgsqlDbType = ResolveNpgsqlDbType(type);
            if (npgsqlDbType != null)
            {
                return npgsqlDbType.Value;
            }

            if (type.IsNullable()) return ToDbType(type.GetInnerTypeFromNullable());

            if (type.IsEnum) return NpgsqlDbType.Integer;

            if (type.IsArray)
            {
                if (type == typeof(byte[]))
                    return NpgsqlDbType.Bytea;
                return NpgsqlDbType.Array | ToDbType(type.GetElementType());
            }

            var typeInfo = type.GetTypeInfo();

            var ilist = typeInfo.ImplementedInterfaces.FirstOrDefault(x => x.GetTypeInfo().IsGenericType && x.GetGenericTypeDefinition() == typeof(IList<>));
            if (ilist != null)
                return NpgsqlDbType.Array | ToDbType(ilist.GetGenericArguments()[0]);

            if (typeInfo.IsGenericType && type.GetGenericTypeDefinition() == typeof(NpgsqlRange<>))
                return NpgsqlDbType.Range | ToDbType(type.GetGenericArguments()[0]);

            if (type == typeof(DBNull))
                return NpgsqlDbType.Unknown;

            throw new NotSupportedException("Can't infer NpgsqlDbType for type " + type);
        }

        public static string GetPgType(Type memberType, EnumStorage enumStyle)
        {
            if (memberType.IsEnum)
            {
                return enumStyle == EnumStorage.AsInteger ? "integer" : "varchar";
            }

            if (memberType.IsArray)
            {
                return GetPgType(memberType.GetElementType(), enumStyle) + "[]";
            }

            if (memberType.IsNullable())
            {
                return GetPgType(memberType.GetInnerTypeFromNullable(), enumStyle);
            }

            if (memberType.IsConstructedGenericType)
            {
                var templateType = memberType.GetGenericTypeDefinition();
                return ResolvePgType(templateType) ?? "jsonb";
            }

            return ResolvePgType(memberType) ?? "jsonb";
        }

        public static bool HasTypeMapping(Type memberType)
        {
            if (memberType.IsNullable())
            {
                return HasTypeMapping(memberType.GetInnerTypeFromNullable());
            }

            // more complicated later
            return ResolvePgType(memberType) != null || memberType.IsEnum;
        }

        public static string ApplyCastToLocator(this string locator, EnumStorage enumStyle, Type memberType)
        {
            if (memberType.IsEnum)
            {
                return enumStyle == EnumStorage.AsInteger ? "({0})::int".ToFormat(locator) : locator;
            }

            // Treat "unknown" PgTypes as jsonb (this way null checks of arbitary depth won't fail on cast).
            return "CAST({0} as {1})".ToFormat(locator, GetPgType(memberType, enumStyle));
        }

        public static bool IsDate(this object value)
        {
            if (value == null) return false;

            var type = value.GetType();

            return type == typeof(DateTime) || type == typeof(DateTime?);
        }
    }
}
