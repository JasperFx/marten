using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Xunit;

namespace EventSourcingTests.Bugs;

public class nulls_in_event_name_cache : BugIntegrationContext
{
    [Fact]
    public async Task rebuild_with_unregistered_events_does_not_cause_null_ref()
    {
        var stream = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(stream, new MembersJoined(), new MembersDeparted());
            await session.SaveChangesAsync();
        }

        await using var store = SeparateStore(opts =>
        {
            opts.DatabaseSchemaName = theStore.Options.DatabaseSchemaName;
            opts.Events.StreamIdentity = StreamIdentity.AsGuid;
            opts.Projections.Add<MemberJoinedProjection>(ProjectionLifecycle.Inline);
            opts.Projections.Add(new CustomProjection(), ProjectionLifecycle.Async);
            opts.Connection(ConnectionSource.ConnectionString);
        });

        var daemon = await store.BuildProjectionDaemonAsync();

        // Populate EventGraph name cache with null event mappings by requesting a projection with no event restrictions
        await daemon.RebuildProjectionAsync("EventSourcingTests.Bugs.CustomProjection", CancellationToken.None);
        // Request a rebuild from a projection that uses the event filter
        await daemon.RebuildProjectionAsync<MemberJoinedProjection>(CancellationToken.None);
    }
}

public record MemberJoinedOnly(Guid Id);

public sealed class MemberJoinedProjection: SingleStreamProjection<MemberJoinedOnly>
{
    public MemberJoinedProjection()
    {
        CreateEvent<MembersJoined>(x => new MemberJoinedOnly(x.QuestId));
    }
}

public class CustomProjection: IProjection
{
    public Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams, CancellationToken cancellation)
    {
        return Task.CompletedTask;
    }
}

