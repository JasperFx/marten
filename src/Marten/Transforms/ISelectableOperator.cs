using Marten.Linq;
using Marten.Schema;
using Marten.Storage;

namespace Marten.Transforms
{
    public interface ISelectableOperator
    {
        ISelector<T> BuildSelector<T>(string dataLocator, ITenant schema, IQueryableDocument document);
    }
}