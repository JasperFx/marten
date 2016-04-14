using System;
using Marten.Schema;

namespace Marten.Linq.Results
{
    public class SingleOrDefaultHandler<T> : OnlyOneResultHandler<T>
    {
        public SingleOrDefaultHandler(DocumentQuery query, IDocumentSchema schema) : base(2, query, schema)
        {
        }

        protected override void assertMoreResults()
        {
            // TODO -- the message might be wrong
            throw new InvalidOperationException("Sequence contains more than one element");
        }
    }
}