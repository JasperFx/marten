using System;

namespace Marten.Linq.Results
{
    public class FirstHandler<T> : OnlyOneResultHandler<T>
    {
        public FirstHandler(DocumentQuery query) : base(1, query)
        {
        }

        protected override T defaultValue()
        {
            // TODO -- the message might be wrong
            throw new InvalidOperationException("Sequence contained no elements");
        }
    }
}