using System;
using Npgsql;
using Newtonsoft.Json.Serialization;

namespace Marten.Util
{
    public static class StringExtensionMethods
    {
        public static string ReplaceFirst(this string text, string search, string replace)
        {
            int pos = text.IndexOf(search);
            if (pos < 0)
            {
                return text;
            }

            return text.Substring(0, pos) + replace + text.Substring(pos + search.Length);
        }

        public static string UseParameter(this string text, NpgsqlParameter parameter)
        {
            return text.ReplaceFirst("?", ":" + parameter.ParameterName);
        }

        public static bool Contains(this string source, string value, StringComparison comparison)
        {
            return source.IndexOf(value, comparison) >= 0;
        }

        public static string ToCamelCase(this string s)
        {
            if (string.IsNullOrEmpty(s) || !char.IsUpper(s[0]))
            {
                return s;
            }

            char[] chars = s.ToCharArray();

            for (int i = 0; i < chars.Length; i++)
            {
                if (i == 1 && !char.IsUpper(chars[i]))
                {
                    break;
                }

                bool hasNext = (i + 1 < chars.Length);
                if (i > 0 && hasNext && !char.IsUpper(chars[i + 1]))
                {
                    break;
                }

                chars[i] = char.ToLowerInvariant(chars[i]);
            }

            return new string(chars);
        }

        private static readonly SnakeCaseNamingStrategy _snakeCaseNamingStrategy = new SnakeCaseNamingStrategy();
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
}