using System;
using System.Diagnostics;
using Baseline;
using Marten.Events;
using Marten.Events.V4Concept;
using Marten.Storage;
using NSubstitute;

namespace Marten.Testing.Events.V4Concepts.Aggregations
{
    public class TestEventScenario
    {
        public readonly Cache<Guid, TestEventSlice> Streams = new Cache<Guid, TestEventSlice>(id => new TestEventSlice(id));
    }

    public class TestEventSlice: EventSlice<MyAggregate, Guid>
    {
        public TestEventSlice(Guid id) : base(id, Substitute.For<ITenant>())
        {
        }

        public bool IsNew { get; set; }

        public Event<AEvent> A() => Add<AEvent>();

        public Event<BEvent> B() => Add<BEvent>();

        public Event<CEvent> C() => Add<CEvent>();

        public Event<DEvent> D() => Add<DEvent>();

        public Event<EEvent> E() => Add<EEvent>();

        public Event<T> Add<T>() where T : new()
        {
            var @event = new Event<T>(new T());
            AddEvent(@event);

            return @event;
        }

        public IEvent Add<T>(T @event)
        {
            var item = new Event<T>(@event);
            AddEvent(item);

            return item;
        }
    }

    public class MyAggregate
    {
        public Guid Id { get; set; }

        public int ACount { get; set; }
        public int BCount { get; set; }
        public int CCount { get; set; }
        public int DCount { get; set; }
        public int ECount { get; set; }

        public string Created { get; set; }
        public string UpdatedBy { get; set; }
        public Guid EventId { get; set; }
    }

    public class AEvent {}
    public class BEvent {}
    public class CEvent {}
    public class DEvent {}
    public class EEvent {}

    public class CreateEvent
    {
        public int A { get; }
        public int B { get; }
        public int C { get; }
        public int D { get; }

        public CreateEvent(int a, int b, int c, int d)
        {
            A = a;
            B = b;
            C = c;
            D = d;
        }
    }

    public class CreateEvent2
    {
        public int A { get; }
        public int B { get; }
        public int C { get; }
        public int D { get; }

        public CreateEvent2(int a, int b, int c, int d)
        {
            A = a;
            B = b;
            C = c;
            D = d;
        }
    }



    public class AlternativeCreateEvent
    {

    }
}
