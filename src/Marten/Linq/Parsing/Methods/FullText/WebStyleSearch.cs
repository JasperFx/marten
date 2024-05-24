namespace Marten.Linq.Parsing.Methods.FullText;

internal class WebStyleSearch: FullTextSearchMethodCallParser
{
    public WebStyleSearch(): base(nameof(LinqExtensions.WebStyleSearch), FullTextSearchFunction.websearch_to_tsquery)
    {
    }
}
