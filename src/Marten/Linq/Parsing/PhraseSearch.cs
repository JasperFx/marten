namespace Marten.Linq.Parsing
{
    public class PhraseSearch : FullTextSearchMethodCallParser
    {
        public PhraseSearch() : base(nameof(LinqExtensions.Search), "phraseto_tsquery")
        {
        }
    }
}