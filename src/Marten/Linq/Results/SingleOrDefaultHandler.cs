using System;

namespace Marten.Linq.Results
{
    public class SingleOrDefaultHandler<T> : OnlyOneResultHandler<T>
    {
        public SingleOrDefaultHandler(DocumentQuery query) : base(2, query)
        {
        }

        protected override void assertMoreResults()
        {
            // TODO -- the message might be wrong
            throw new InvalidOperationException("Sequence contains more than one element");
        }
    }
}