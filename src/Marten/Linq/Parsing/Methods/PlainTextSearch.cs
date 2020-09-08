namespace Marten.Linq.Parsing.Methods
{
    internal class PlainTextSearch: FullTextSearchMethodCallParser
    {
        public PlainTextSearch() : base(nameof(LinqExtensions.PlainTextSearch), FullTextSearchFunction.plainto_tsquery)
        {
        }
    }
}
