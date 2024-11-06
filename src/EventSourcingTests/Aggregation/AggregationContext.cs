using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.CodeGeneration;
using JasperFx.Events;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Testing.Harness;
using Xunit;

namespace EventSourcingTests.Aggregation;

public class AggregationContext : IntegrationContext, IAsyncLifetime
{
    protected SingleStreamProjection<MyAggregate> _projection;

    public AggregationContext(DefaultStoreFixture fixture) : base(fixture)
    {

    }

    protected override async Task fixtureSetup()
    {
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(MyAggregate));
    }

    public void UsingDefinition<T>() where T : SingleStreamProjection<MyAggregate>, new()
    {
        _projection = new T();

        var rules = theStore.Options.CreateGenerationRules();
        rules.TypeLoadMode = TypeLoadMode.Dynamic;
        _projection.Compile(theStore.Options, rules);
    }

    public void UsingDefinition(Action<SingleStreamProjection<MyAggregate>> configure)
    {
        _projection = new SingleStreamProjection<MyAggregate>();
        configure(_projection);


        var rules = theStore.Options.CreateGenerationRules();
        rules.TypeLoadMode = TypeLoadMode.Dynamic;
        _projection.Compile(theStore.Options, rules);
    }


    public ValueTask<MyAggregate> LiveAggregation(Action<TestEventSlice> action)
    {
        var fragment = BuildStreamFragment(action);

        var aggregator = _projection.BuildLiveAggregator();
        var events = (IReadOnlyList<IEvent>)fragment.Events();
        return aggregator.BuildAsync(events, theSession, null, CancellationToken.None);
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
            .Select(x => StreamAction.Append(x.Key, x.Value.Events().ToArray()))
            .ToArray();

        var inline = _projection.BuildRuntime(theStore);

        await inline.ApplyAsync(theSession, streams, CancellationToken.None);
        await theSession.SaveChangesAsync();
    }
}
