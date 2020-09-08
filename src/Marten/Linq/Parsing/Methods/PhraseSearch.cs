namespace Marten.Linq.Parsing.Methods
{
    internal class PhraseSearch: FullTextSearchMethodCallParser
    {
        public PhraseSearch() : base(nameof(LinqExtensions.PhraseSearch), FullTextSearchFunction.phraseto_tsquery)
        {
        }
    }
}
