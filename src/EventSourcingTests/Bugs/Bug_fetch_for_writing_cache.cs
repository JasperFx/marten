using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Schema;
using Marten.Testing.Harness;
using System;
using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_fetch_for_writing_cache: BugIntegrationContext
{
    [Fact]
    public async Task Test()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Projections.Add<TestProjection2>(ProjectionLifecycle.Inline);
            opts.Schema
                .For<TestAggregate>()
                .Identity(x => x.StreamKey);
        });

        var streamKey = Guid.NewGuid().ToString();

        theSession.Events.StartStream(streamKey, new NamedEvent2("foo"));
        await theSession.SaveChangesAsync();

        var test = await theSession.Events.FetchForWriting<TestAggregate>(streamKey);
        test.Aggregate.Name.ShouldBe("foo");

        test.AppendOne(new NamedEvent2("bar"));
        //await theSession.Events.AppendOptimistic(streamKey, new NamedEvent2("bar")); If I commented the two lines above and uncommented this one it works fine
        await theSession.SaveChangesAsync();

        test = await theSession.Events.FetchForWriting<TestAggregate>(streamKey);
        test.Aggregate.Name.ShouldBe("bar");
    }

}

public record NamedEvent2(string Name);

public class TestProjection2: SingleStreamProjection<TestAggregate>
{
    public TestAggregate Create(NamedEvent2 @event)
    => new TestAggregate(@event.Name);

    public TestAggregate Apply(NamedEvent2 @event, TestAggregate aggregate)
    => aggregate with { Name = @event.Name };
}

public record TestAggregate
{
    public TestAggregate(string name)
    {
        Name = name;
    }

    [Identity]
    public string StreamKey { get; set; } = null!;

    public string Name { get; set; } = null!;
}
