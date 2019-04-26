namespace Marten.Linq.Parsing
{
    public class WebSearch : FullTextSearchMethodCallParser
    {
        public WebSearch() : base(nameof(LinqExtensions.WebSearch), FullTextSearchFunction.websearch_to_tsquery)
        {
        }
    }
}