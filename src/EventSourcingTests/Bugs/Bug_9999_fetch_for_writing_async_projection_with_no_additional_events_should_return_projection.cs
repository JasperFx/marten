using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Events.CodeGeneration;
using Marten.Events.Projections;
using Marten.Schema;
using Marten.Testing.Harness;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_9999_fetch_for_writing_async_projection_with_no_additional_events_should_return_projection : BugIntegrationContext
{
    // ! IMPORTANT - this does not fail on the bug - for some reason the dynamic codegen doesn't produce the error.
    // TODO: Rework this to properly highlight the error
    [Fact]
    public async Task override_the_optimistic_concurrency_on_projected_document()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Projections.Snapshot<TestProjection>(SnapshotLifecycle.Async);
        });

        var streamKey = Guid.NewGuid().ToString();

        theSession.Events.StartStream(streamKey, new NamedEvent("foo"), new EventB());
        await theSession.SaveChangesAsync();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.RebuildProjectionAsync<TestProjection>(CancellationToken.None);

        var result = await theSession.Events.FetchForWriting<TestProjection>(streamKey);

        Assert.Equal(2, result.CurrentVersion);
        Assert.Equal(2, result.StartingVersion);
        Assert.NotNull(result.Aggregate);
        Assert.Equal(streamKey, result.Aggregate.StreamKey);
        // TODO: There is a weird bug here where ~25% of the time this is set to null. Seems to happen intermittently across all frameworks. No idea why.
        Assert.Equal("foo", result.Aggregate.Name);
    }



}

public record NamedEvent(string Name);

// put it here to avoid a bug with rebuilding projections whose types are nested classes
// TODO: Write up this bug
public record TestProjection
{
    public TestProjection()
    {
        Debug.WriteLine("Called with default Ctor");
    }

    [Identity]
    public string StreamKey { get; set; } = null!;

    public string Name { get; set; } = null!;

    public static TestProjection Create(NamedEvent @event) => new()
    {
        Name = @event.Name
    };
}
