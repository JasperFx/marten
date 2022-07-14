using System.Text.RegularExpressions;

namespace Marten.Events.Projections.Flattened
{
    internal static class KebabConverter
    {
        // Shamelessly stolen from https://stackoverflow.com/questions/37301287/how-do-i-convert-pascalcase-to-kebab-case-with-c

        
        public static string ToKebabCase(this string str)
        {
            // find and replace all parts that starts with one capital letter e.g. Net
            var str1 = Regex.Replace(str, "[A-Z][a-z]+", m => $"-{m.ToString().ToLower()}");

            // find and replace all parts that are all capital letter e.g. NET
            var str2 = Regex.Replace(str1, "[A-Z]+", m => $"-{m.ToString().ToLower()}");

            return str2.TrimStart('-');
        }
    }
}
