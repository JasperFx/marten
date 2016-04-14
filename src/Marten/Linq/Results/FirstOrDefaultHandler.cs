using Marten.Schema;

namespace Marten.Linq.Results
{
    public class FirstOrDefaultHandler<T> : OnlyOneResultHandler<T>
    {
        public FirstOrDefaultHandler(DocumentQuery query, IDocumentSchema schema) : base(1, query, schema)
        {
        }
    }
}