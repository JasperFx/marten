namespace Marten.Linq.Parsing.Methods.FullText;

internal class Search: FullTextSearchMethodCallParser
{
    public Search(): base(nameof(LinqExtensions.Search), FullTextSearchFunction.to_tsquery)
    {
    }
}
