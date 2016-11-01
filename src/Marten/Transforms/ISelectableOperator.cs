using Marten.Linq;
using Marten.Schema;

namespace Marten.Transforms
{
    public interface ISelectableOperator
    {
        ISelector<T> BuildSelector<T>(string dataLocator, IDocumentSchema schema, IQueryableDocument document);
    }
}