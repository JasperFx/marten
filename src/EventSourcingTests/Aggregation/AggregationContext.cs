using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.CodeGeneration;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Testing.Harness;

namespace EventSourcingTests.Aggregation;

public class AggregationContext : IntegrationContext
{
    protected SingleStreamProjection<MyAggregate, Guid> _projection;

    public AggregationContext(DefaultStoreFixture fixture) : base(fixture)
    {

    }

    protected override Task fixtureSetup()
    {
        return theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(MyAggregate));
    }

    public void UsingDefinition<T>() where T : SingleStreamProjection<MyAggregate, Guid>, new()
    {
        _projection = new T();

        var rules = theStore.Options.CreateGenerationRules();
        rules.TypeLoadMode = TypeLoadMode.Dynamic;
    }

    public void UsingDefinition(Action<SingleStreamProjection<MyAggregate, Guid>> configure)
    {
        _projection = new SingleStreamProjection<MyAggregate, Guid>();
        configure(_projection);


        var rules = theStore.Options.CreateGenerationRules();
        rules.TypeLoadMode = TypeLoadMode.Dynamic;
    }


    public ValueTask<MyAggregate> LiveAggregation(Action<TestEventSlice> action)
    {
        var fragment = BuildStreamFragment(action);
        var aggregator = _projection.As<IAggregatorSource<IQuerySession>>().Build<MyAggregate>();
        var events = fragment.Events();
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

        var inline = _projection.As<IProjectionSource<IDocumentOperations, IQuerySession>>().BuildForInline();

        await inline.ApplyAsync(theSession, streams, CancellationToken.None);
        await theSession.SaveChangesAsync();
    }
}
