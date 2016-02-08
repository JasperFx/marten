using System;
using Npgsql;

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
    }
}