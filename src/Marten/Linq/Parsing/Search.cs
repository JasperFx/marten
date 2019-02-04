namespace Marten.Linq.Parsing
{
    public class Search : FullTextSearchMethodCallParser
    {
        public Search() : base(nameof(LinqExtensions.Search), "to_tsquery")
        {
        }
    }
}