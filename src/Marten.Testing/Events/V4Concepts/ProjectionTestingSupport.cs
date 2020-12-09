using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Events;
using Marten.Events.V4Concept;
using Marten.Events.V4Concept.Aggregation;
using Marten.Events.V4Concept.CodeGeneration;
using Marten.Internal;
using Marten.Storage;
using Marten.Testing.Harness;
using NSubstitute;

namespace Marten.Testing.Events.V4Concepts
{
    public class AggregationContext : IntegrationContext
    {
        protected V4AggregateProjection<MyAggregate> _projection;
        private V4Aggregator<MyAggregate, Guid> _aggregator;

        public AggregationContext(DefaultStoreFixture fixture) : base(fixture)
        {
            theStore.Advanced.Clean.DeleteDocumentsFor(typeof(MyAggregate));
        }

        public void UsingDefinition<T>() where T : V4AggregateProjection<MyAggregate>, new()
        {
            _projection = new T();

            _projection.Compile(theStore);
        }


        public ValueTask<MyAggregate> LiveAggregation(Action<TestStreamFragment> action)
        {
            var fragment = BuildStreamFragment(action);

            var aggregator = _projection.BuildLiveAggregator();

            return aggregator.BuildAsync((IReadOnlyList<IEvent>) fragment.Events, theSession, CancellationToken.None);
        }


        public static TestStreamFragment BuildStreamFragment(Action<TestStreamFragment> action)
        {
            var fragment = new TestStreamFragment(Guid.NewGuid());
            action(fragment);
            return fragment;
        }

        public V4Aggregator<MyAggregate, Guid> theAggregator
        {
            get
            {
                return _aggregator ??= (V4Aggregator<MyAggregate, Guid>)_projection.BuildLiveAggregator();
            }
        }

        public async Task InlineProject(Action<TestEventScenario> action)
        {
            var scenario = new TestEventScenario();
            action(scenario);

            var streams = scenario
                .Streams
                .ToDictionary()
                .Select(x => StreamAction.Append(x.Key, x.Value.Events.ToArray()))
                .ToArray();

            var inline = _projection.BuildInlineProjection((IMartenSession) theSession);

            await inline.ApplyAsync(theSession, streams, CancellationToken.None);
            await theSession.SaveChangesAsync();
        }
    }

    public class TestEventScenario
    {
        public readonly Cache<Guid, TestStreamFragment> Streams = new Cache<Guid, TestStreamFragment>(id => new TestStreamFragment(id));
    }

    public class TestStreamFragment: StreamFragment<MyAggregate, Guid>
    {
        public TestStreamFragment(Guid id) : base(id, Substitute.For<ITenant>())
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
            Events.Add(@event);

            return @event;
        }

        public IEvent Add<T>(T @event)
        {
            var item = new Event<T>(@event);
            Events.Add(item);

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

    public class AlternativeCreateEvent
    {

    }
}
