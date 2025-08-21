using System.Text.RegularExpressions;

namespace Marten.Events.Projections.Flattened;

internal static class KebabConverter
{

    public static string ToKebabCase(this string str)
    {
        // find and replace all parts that starts with one capital letter e.g. Net
        var str1 = KebabConverterRegexExpressions.PascalWordRegex().Replace(str, m => $"-{m.ToString().ToLower()}");

        // find and replace all parts that are all capital letter e.g. NET
        var str2 = KebabConverterRegexExpressions.AllCapsWordRegex().Replace(str1, m => $"-{m.ToString().ToLower()}");

        return str2.TrimStart('-');
    }
}

internal static partial class KebabConverterRegexExpressions
{
    [GeneratedRegex("[A-Z][a-z]+")]
    internal static partial Regex PascalWordRegex();

    [GeneratedRegex("[A-Z]+")]
    internal static partial Regex AllCapsWordRegex();
}
