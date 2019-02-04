namespace Marten.Linq.Parsing
{
    public class PlainTextSearch : FullTextSearchMethodCallParser
    {
        public PlainTextSearch() : base(nameof(LinqExtensions.Search), "plainto_tsquery")
        {
        }
    }
}