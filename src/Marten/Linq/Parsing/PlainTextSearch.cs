namespace Marten.Linq.Parsing
{
    public class PlainTextSearch : FullTextSearchMethodCallParser
    {
        public PlainTextSearch() : base(nameof(LinqExtensions.PlainTextSearch), "plainto_tsquery")
        {
        }
    }
}