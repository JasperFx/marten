using System;
using Marten.Events.V4Concept.Aggregation;

namespace Marten.Events.V4Concept
{
    // TODO -- well, everything. Add Xml comments
    public class V4ProjectionCollection
    {
        internal V4ProjectionCollection(StoreOptions options)
        {

        }

        public void Inline(IInlineProjection projection)
        {
            throw new NotImplementedException();
        }

        public void Async(IInlineProjection projection)
        {
            throw new NotImplementedException();
        }

        public void Inline(EventProjection projection)
        {
            throw new NotImplementedException();
        }

        public void Async(EventProjection projection)
        {
            throw new NotImplementedException();
        }

        public void InlineSelfAggregate<T>()
        {
            throw new NotImplementedException();
        }

        public void AsyncSelfAggregate<T>()
        {
            throw new NotImplementedException();
        }

        public void InlineView<TDoc, TId>(V4ViewProjection<TDoc, TId> projection)
        {
            throw new NotImplementedException();
        }

        public void AsyncView<TDoc, TId>(V4ViewProjection<TDoc, TId> projection)
        {
            throw new NotImplementedException();
        }

        public void InlineAggregation<T>(V4AggregateProjection<T> projection)
        {
            throw new NotImplementedException();
        }

        public void AsyncAggregation<T>(V4AggregateProjection<T> projection)
        {
            throw new NotImplementedException();
        }
    }
}
