#nullable enable
namespace Marten.Linq.Parsing.Methods.FullText;

internal class PlainTextSearch: FullTextSearchMethodCallParser
{
    public PlainTextSearch(): base(nameof(LinqExtensions.PlainTextSearch), FullTextSearchFunction.plainto_tsquery)
    {
    }
}
