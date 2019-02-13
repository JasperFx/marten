namespace Marten.Linq.Parsing
{
    public class PhraseSearch : FullTextSearchMethodCallParser
    {
        public PhraseSearch() : base(nameof(LinqExtensions.PhraseSearch), FullTextSearchFunction.phraseto_tsquery)
        {
        }
    }
}