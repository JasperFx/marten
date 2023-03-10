using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Xunit;
using Xunit.Abstractions;

namespace Marten.AsyncDaemon.Testing.Bugs;

public class Bug_sequential_rebuilds_throws_internally: BugIntegrationContext
{
    private readonly ITestOutputHelper _output;

    public Bug_sequential_rebuilds_throws_internally(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task rebuild_throw_reproduction()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add<RandomProjection.Projector>(ProjectionLifecycle.Inline);
        });

        var stream = Guid.NewGuid();

        theSession.Events.StartStream(stream,
            new CreatedEvent(Guid.NewGuid(), Guid.NewGuid().ToString()));

        await theSession.SaveChangesAsync();

        var events = new List<UpdatedEvent>();

        for (var i = 0; i < 1000; i++)
        {
            events.Add(new UpdatedEvent(stream, $"event-value-{i}"));
        }

        theSession.Events.Append(stream, events);
        await theSession.SaveChangesAsync();

        using var logger = _output.BuildLogger();
        using var daemon1 = await theStore.BuildProjectionDaemonAsync(logger: logger);
        await daemon1.StartDaemon();

        await daemon1.RebuildProjection("Bug_sequential_rebuilds_throws_internally.RandomProjection", default);

        await daemon1.StopAll();

        await daemon1.StartDaemon();

        await daemon1.RebuildProjection("Bug_sequential_rebuilds_throws_internally.RandomProjection", default);

        await daemon1.StopAll();

        Assert.All(logger.Entries, entry => entry.Exception.ShouldBeNull());
    }

    public record CreatedEvent(Guid Id, string Value);

    public record UpdatedEvent(Guid Id, string Value);

    public record RandomProjection(Guid Id, string Value)
    {
        public class Projector: SingleStreamProjection<RandomProjection>
        {
            public static RandomProjection Create(CreatedEvent @event)
            {
                return new RandomProjection(@event.Id, @event.Value);
            }

            public RandomProjection Apply(UpdatedEvent @event, RandomProjection current)
            {
                return current with { Value = @event.Value };
            }
        }
    }
}
