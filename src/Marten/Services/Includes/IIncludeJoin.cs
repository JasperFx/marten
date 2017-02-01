using System.Text;
using Marten.Linq;
using Marten.Schema;
using Marten.Util;

namespace Marten.Services.Includes
{
    public interface IIncludeJoin
    {
        string JoinText { get; }
        string TableAlias { get; }
        ISelector<TSearched> WrapSelector<TSearched>(IDocumentSchema schema, ISelector<TSearched> inner);

        void AppendJoin(CommandBuilder sql, string rootTableAlias, IQueryableDocument document);
    }
}