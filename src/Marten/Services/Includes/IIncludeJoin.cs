using System.Text;
using Marten.Linq;
using Marten.Schema;
using Marten.Storage;
using Marten.Util;

namespace Marten.Services.Includes
{
    public interface IIncludeJoin
    {
        string JoinText { get; }
        string TableAlias { get; }
        ISelector<TSearched> WrapSelector<TSearched>(StorageFeatures storage, ISelector<TSearched> inner);

        void AppendJoin(CommandBuilder sql, string rootTableAlias, IQueryableDocument document);
    }
}