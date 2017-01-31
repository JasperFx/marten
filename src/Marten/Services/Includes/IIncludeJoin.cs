using Marten.Linq;
using Marten.Schema;

namespace Marten.Services.Includes
{
    public interface IIncludeJoin
    {
        string JoinText { get; }
        string TableAlias { get; }
        ISelector<TSearched> WrapSelector<TSearched>(IDocumentSchema schema, ISelector<TSearched> inner);

        // TODO -- have this take in a StringBuilder instead
        string JoinTextFor(string rootTableAlias, IQueryableDocument document);
    }
}