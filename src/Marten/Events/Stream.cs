using System;
using System.Collections.Generic;

namespace Marten.Events
{
    public class Stream<T> where T : IAggregate
    {
        public Stream(Guid id)
        {
            Id = id;
        }

        public Guid Id { get; }
        public Type AggregateType { get; } = typeof (T);

        public readonly IList<IEvent> Events = new List<IEvent>();
    }
}