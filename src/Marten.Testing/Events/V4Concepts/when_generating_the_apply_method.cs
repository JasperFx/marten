using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LamarCodeGeneration;
using LamarCompiler;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Events.V4Concept.Aggregation;
using Marten.Events.V4Concept.CodeGeneration;
using Marten.Schema;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Events.V4Concepts
{




    public class MyAggregateDefinition: V4AggregateProjection<MyAggregate>
    {
        public MyAggregate Create(CreateEvent @event)
        {
            return new MyAggregate
            {
                ACount = @event.A,
                BCount = @event.B,
                CCount = @event.C,
                DCount = @event.D
            };
        }

        public MyAggregate Create(AlternativeCreateEvent @event)
        {
            return new MyAggregate{CCount = 5};
        }

        public void Apply(AEvent @event, MyAggregate aggregate)
        {
            aggregate.ACount++;
        }

        public MyAggregate Apply(BEvent @event, MyAggregate aggregate)
        {
            return new MyAggregate
            {
                ACount = aggregate.ACount,
                BCount = aggregate.BCount + 1,
                CCount = aggregate.CCount,
                DCount = aggregate.DCount
            };
        }

        public void Apply(MyAggregate aggregate, CEvent @event)
        {
            aggregate.CCount++;
        }

        public MyAggregate Apply(MyAggregate aggregate, DEvent @event)
        {
            return new MyAggregate
            {
                ACount = aggregate.ACount,
                BCount = aggregate.BCount,
                CCount = aggregate.CCount,
                DCount = aggregate.DCount + 1
            };
        }

    }

}
