namespace Marten.Linq.Parsing.Methods.FullText;

internal class PhraseSearch: FullTextSearchMethodCallParser
{
    public PhraseSearch(): base(nameof(LinqExtensions.PhraseSearch), FullTextSearchFunction.phraseto_tsquery)
    {
    }
}
