namespace Marten.Linq.Parsing
{
    public class Search : FullTextSearchMethodCallParser
    {
        public Search() : base(nameof(LinqExtensions.Search), FullTextSearchFunction.to_tsquery)
        {
        }
    }
}