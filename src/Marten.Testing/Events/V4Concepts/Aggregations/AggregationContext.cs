using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Events.V4Concept.Aggregation;
using Marten.Internal;
using Marten.Testing.Harness;

namespace Marten.Testing.Events.V4Concepts.Aggregations
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

        public void UsingDefinition(Action<V4AggregateProjection<MyAggregate>> configure)
        {
            _projection = new V4AggregateProjection<MyAggregate>();
            configure(_projection);

            _projection.Compile(theStore);
        }


        public ValueTask<MyAggregate> LiveAggregation(Action<TestStreamFragment> action)
        {
            var fragment = BuildStreamFragment(action);

            var aggregator = _projection.BuildLiveAggregator();
            return aggregator.BuildAsync((IReadOnlyList<IEvent>)fragment.Events, theSession, null, CancellationToken.None);
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
}
