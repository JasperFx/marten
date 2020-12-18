using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Marten.Events.V4Concept.Aggregation;

namespace Marten.Events.V4Concept
{
    /// <summary>
    /// Project a single document view across events that may span across
    /// event streams in a user-defined grouping
    /// </summary>
    /// <typeparam name="TDoc"></typeparam>
    /// <typeparam name="TId"></typeparam>
    public abstract class V4ViewProjection<TDoc, TId> : V4AggregateProjection<TDoc>
    {
        // TODO -- the split logic will be different. This'll need to override that a bit
        public void Identity<TEvent>(Expression<Func<TEvent, TId>> expression)
        {
            throw new NotImplementedException();
        }

        public void FanOut<TEvent, TChild>(Expression<Func<TEvent, IEnumerable<TChild>>> expression)
        {

            throw new NotImplementedException();
        }
    }
}
