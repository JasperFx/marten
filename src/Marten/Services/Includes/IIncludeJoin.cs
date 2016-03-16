using Marten.Linq;
using Marten.Schema;

namespace Marten.Services.Includes
{
    public interface IIncludeJoin
    {
        string JoinText { get; }
        string TableAlias { get; }
        ISelector<TSearched> WrapSelector<TSearched>(IDocumentSchema schema, ISelector<TSearched> inner);
    }
}