namespace Marten.Linq.Parsing
{
    public class PlainTextSearch : FullTextSearchMethodCallParser
    {
        public PlainTextSearch() : base(nameof(LinqExtensions.PlainTextSearch), FullTextSearchFunction.plainto_tsquery)
        {
        }
    }
}