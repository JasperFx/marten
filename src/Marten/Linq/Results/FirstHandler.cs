using System;
using Marten.Schema;

namespace Marten.Linq.Results
{
    public class FirstHandler<T> : OnlyOneResultHandler<T>
    {
        public FirstHandler(DocumentQuery query, IDocumentSchema schema) : base(1, query, schema)
        {
        }

        protected override T defaultValue()
        {
            // TODO -- the message might be wrong
            throw new InvalidOperationException("Sequence contained no elements");
        }
    }
}