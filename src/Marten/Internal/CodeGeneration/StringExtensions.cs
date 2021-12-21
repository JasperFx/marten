using System;
using System.Linq;
using System.Text.RegularExpressions;
using Baseline;

namespace Marten.Internal.CodeGeneration
{
    public static class StringExtensions
    {
        public static string Sanitize(this string value)
        {
            return Regex.Replace(value, @"[\#\<\>\,\.\]\[\`\+\-]", "_").Replace(" ", "");
        }

        public static string ToTypeNamePart(this Type type)
        {
            if (type.IsGenericType)
            {
                return type.Name.Split('`').First() + "_of_" +
                       type.GetGenericArguments().Select(x => x.ToTypeNamePart()).Join("_");
            }
            else
            {
                return type.Name;
            }
        }
    }
}
