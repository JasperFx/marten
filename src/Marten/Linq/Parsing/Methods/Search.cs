namespace Marten.Linq.Parsing.Methods
{
    internal class Search: FullTextSearchMethodCallParser
    {
        public Search() : base(nameof(LinqExtensions.Search), FullTextSearchFunction.to_tsquery)
        {
        }
    }
}
