namespace Marten.Linq.Parsing
{
    public class WebStyleSearch: FullTextSearchMethodCallParser
    {
        public WebStyleSearch() : base(nameof(LinqExtensions.WebStyleSearch), FullTextSearchFunction.websearch_to_tsquery)
        {
        }
    }
}
