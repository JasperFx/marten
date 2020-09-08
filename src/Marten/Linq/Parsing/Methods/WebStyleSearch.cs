namespace Marten.Linq.Parsing.Methods
{
    internal class WebStyleSearch: FullTextSearchMethodCallParser
    {
        public WebStyleSearch() : base(nameof(LinqExtensions.WebStyleSearch), FullTextSearchFunction.websearch_to_tsquery)
        {
        }
    }
}
