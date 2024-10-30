using System;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Schema;
using Marten.Testing.Harness;
using Xunit;
using Xunit.Abstractions;

namespace DaemonTests.Bugs;

public class Bug_2245_async_daemon_getting_stuck : BugIntegrationContext
{
    private readonly ITestOutputHelper _testOutputHelper;

    public Bug_2245_async_daemon_getting_stuck(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public async Task ErrorOnSyncProjection_ShouldNotBreakAsyncProjection()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Projections.Add<SyncProjection.Projector>(ProjectionLifecycle.Inline);
            opts.Projections.Add<AsyncProjection.Projector>(ProjectionLifecycle.Async);
        });

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        // prove async daemon works on happy path
        theSession.Events.StartStream(Guid.NewGuid().ToString(),
            new CreatedEvent(Guid.NewGuid().ToString(), Guid.NewGuid().ToString()));
        await theSession.SaveChangesAsync();

        await daemon.WaitForNonStaleData(10.Seconds());


        // unhappy path, force a scenario where a poison event breaks inline projections
        // should not cause async projections to get stuck after event is rolled back.
        try
        {

            theSession.Events.StartStream(Guid.NewGuid().ToString(),
                new UpdatedEvent(Guid.NewGuid().ToString(), Guid.NewGuid().ToString()));

            await theSession.SaveChangesAsync();
        }
        catch (ApplyEventException e)
        {
            // this should fail on save as the sync projection does not handle an updated event correctly.
            _testOutputHelper.WriteLine(e.ToString());
        }

        // should be happy, but gets stuck trying to reach highwater mark
        // see console output of async daemon
        await daemon.WaitForNonStaleData(10.Seconds());

        // however, writing any new event will unstick the async daemon
        // comment out previous line of code and test will pass
        var newSession = theStore.LightweightSession();

        newSession.Events.StartStream(Guid.NewGuid().ToString(),
            new CreatedEvent(Guid.NewGuid().ToString(), Guid.NewGuid().ToString()));
        await newSession.SaveChangesAsync();

        await daemon.WaitForNonStaleData(10.Seconds());
    }

    public record CreatedEvent(string Key, string Value);
    public record UpdatedEvent(string Key, string Value);

    public record SyncProjection([property: Identity] string Key, string Value)
    {
        public class Projector : MultiStreamProjection<SyncProjection, string>
        {
            public Projector()
            {
                Identity<CreatedEvent>(e => e.Key);
                Identity<UpdatedEvent>(e => e.Key);
            }

            public static SyncProjection Create(CreatedEvent @event)
            {
                return new SyncProjection(@event.Key, @event.Value);
            }

            public SyncProjection Apply(UpdatedEvent @event, SyncProjection current)
            {
                return current with
                {
                    Value = @event.Value
                };
            }
        }
    }


    public record UnrelatedEvent(string Key, string Value);
    public record AsyncProjection([property: Identity] string Key, string Value)
    {
        public class Projector : MultiStreamProjection<AsyncProjection, string>
        {
            public Projector()
            {
                Identity<UnrelatedEvent>(e => e.Key);
            }

            public static AsyncProjection Create(UnrelatedEvent @event)
            {
                return new AsyncProjection(@event.Key, @event.Value);
            }
        }
    }
}
