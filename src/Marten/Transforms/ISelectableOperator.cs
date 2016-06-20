using Marten.Linq;
using Marten.Schema;

namespace Marten.Transforms
{
    public interface ISelectableOperator
    {
        ISelector<T> BuildSelector<T>(IDocumentSchema schema, IQueryableDocument document);
    }
}