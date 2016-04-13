using System;

namespace Marten.Linq.Results
{
    public class SingleHandler<T> : OnlyOneResultHandler<T>
    {
        public SingleHandler(DocumentQuery query) : base(2, query)
        {
        }


        protected override void assertMoreResults()
        {
            // TODO -- the message might be wrong
            throw new InvalidOperationException("Sequence contains more than one element");
        }

        protected override T defaultValue()
        {
            // TODO -- the message might be wrong
            throw new InvalidOperationException("Sequence contained no elements");
        }
    }
}