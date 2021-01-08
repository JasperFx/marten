using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Testing.Harness;

namespace Marten.Testing.Events.Aggregation
{
    public class AggregationContext : IntegrationContext
    {
        protected AggregateProjection<MyAggregate> _projection;

        public AggregationContext(DefaultStoreFixture fixture) : base(fixture)
        {
            theStore.Advanced.Clean.DeleteDocumentsFor(typeof(MyAggregate));
        }

        public void UsingDefinition<T>() where T : AggregateProjection<MyAggregate>, new()
        {
            _projection = new T();

            _projection.Compile(theStore.Options);
        }

        public void UsingDefinition(Action<AggregateProjection<MyAggregate>> configure)
        {
            _projection = new AggregateProjection<MyAggregate>();
            configure(_projection);

            _projection.Compile(theStore.Options);
        }


        public ValueTask<MyAggregate> LiveAggregation(Action<TestEventSlice> action)
        {
            var fragment = BuildStreamFragment(action);

            var aggregator = _projection.BuildLiveAggregator();
            return aggregator.BuildAsync((IReadOnlyList<IEvent>)fragment.Events, theSession, null, CancellationToken.None);
        }


        public static TestEventSlice BuildStreamFragment(Action<TestEventSlice> action)
        {
            var fragment = new TestEventSlice(Guid.NewGuid());
            action(fragment);
            return fragment;
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

            var inline = _projection.BuildInlineProjection(theStore);

            await inline.ApplyAsync(theSession, streams, CancellationToken.None);
            await theSession.SaveChangesAsync();
        }
    }
}
