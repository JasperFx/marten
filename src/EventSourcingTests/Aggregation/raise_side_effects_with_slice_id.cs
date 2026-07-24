using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Internal.Sessions;
using Marten.Services;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Aggregation;

public class raise_side_effects_with_slice_id: OneOffConfigurationsContext
{
    [Fact]
    public async Task recovers_the_slice_identity_when_the_snapshot_was_deleted()
    {
        var outbox = new RecordingSliceIdOutbox();

        StoreOptions(opts =>
        {
            opts.Projections.Add<TeamRosterProjection>(ProjectionLifecycle.Async);
            opts.Events.MessageOutbox = outbox;
        });

        await theStore.Advanced.Clean.DeleteAllDocumentsAsync();
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();

        var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var teamId = Guid.NewGuid();

        // Each member joins from their OWN event stream. The projection groups all
        // of them under the shared TeamId, so the slice identity (TeamId) is not the
        // same as any single stream id.
        theSession.Events.StartStream(Guid.NewGuid(), new MemberJoinedTeam(teamId));
        theSession.Events.StartStream(Guid.NewGuid(), new MemberJoinedTeam(teamId));
        await theSession.SaveChangesAsync();

        await daemon.WaitForNonStaleData(30.Seconds());

        var roster = await theSession.LoadAsync<TeamRoster>(teamId);
        roster.MemberCount.ShouldBe(2);

        // Disband the team from yet another stream. This triggers DeleteEvent<TeamDisbanded>(),
        // so the snapshot is null inside RaiseSideEffects.
        theSession.Events.StartStream(Guid.NewGuid(), new TeamDisbanded(teamId));
        await theSession.SaveChangesAsync();

        await daemon.WaitForNonStaleData(30.Seconds());

        (await theSession.LoadAsync<TeamRoster>(teamId)).ShouldBeNull();

        // The message was published for the deleted slice using the id parameter,
        // even though slice.Snapshot was null.
        outbox
            .Batches
            .SelectMany(x => x.Messages)
            .Select(x => x.message)
            .OfType<NotifyTeamClosed>()
            .Single()
            .TeamId.ShouldBe(teamId);
    }
}

#region sample_raise_side_effects_with_slice_id

// A running count of the active members of a team, aggregated across many
// separate per-member event streams by TeamId.
public class TeamRoster
{
    public Guid Id { get; set; }
    public int MemberCount { get; set; }
}

public record MemberJoinedTeam(Guid TeamId);
public record MemberLeftTeam(Guid TeamId);
public record TeamDisbanded(Guid TeamId);

// The message we want to publish when a team is closed out
public record NotifyTeamClosed(Guid TeamId);

public partial class TeamRosterProjection: MultiStreamProjection<TeamRoster, Guid>
{
    public TeamRosterProjection()
    {
        // All three event types are grouped by TeamId, which becomes the
        // identity of each slice/aggregate.
        Identity<MemberJoinedTeam>(x => x.TeamId);
        Identity<MemberLeftTeam>(x => x.TeamId);
        Identity<TeamDisbanded>(x => x.TeamId);
    }

    public void Apply(TeamRoster roster, MemberJoinedTeam _) => roster.MemberCount++;
    public void Apply(TeamRoster roster, MemberLeftTeam _) => roster.MemberCount--;

    // When a team is disbanded the aggregate is deleted, so the snapshot handed to
    // RaiseSideEffects below is null.
    public bool ShouldDelete(TeamDisbanded _) => true;

    // NEW in JasperFx.Events 2.35.0: the second parameter hands you the identity
    // of the current slice even when its snapshot has been deleted in this batch.
    public override ValueTask RaiseSideEffects(IDocumentOperations operations, Guid id,
        IEventSlice<TeamRoster> slice)
    {
        if (slice.Snapshot == null)
        {
            // The aggregate was deleted (TeamDisbanded -> ShouldDelete(...) == true),
            // so there is no snapshot to read the identity from. Before this overload,
            // there was no reliable way to recover the team identity here -- the slice
            // groups events from many different streams, so slice.Events().First().StreamId
            // is a *member* stream id, not the TeamId. The id parameter is the slice
            // identity (the TeamId) regardless of whether the snapshot still exists.
            slice.PublishMessage(new NotifyTeamClosed(id));
            return new ValueTask();
        }

        // Normal, non-deleted processing still has full access to the current snapshot
        // (and, of course, to id)...

        return new ValueTask();
    }
}

#endregion

public class RecordingSliceIdOutbox: IMessageOutbox
{
    public readonly List<RecordingSliceIdBatch> Batches = new();

    public ValueTask<IMessageBatch> CreateBatch(DocumentSessionBase session)
    {
        var batch = new RecordingSliceIdBatch();
        Batches.Add(batch);
        return new ValueTask<IMessageBatch>(batch);
    }
}

public record SliceIdTenantMessage(string tenantId, object message);

public class RecordingSliceIdBatch: IMessageBatch
{
    public readonly List<SliceIdTenantMessage> Messages = new();

    public Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
        => Task.CompletedTask;

    public Task BeforeCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
        => Task.CompletedTask;

    public ValueTask PublishAsync<T>(T message, string tenantId)
    {
        Messages.Add(new SliceIdTenantMessage(tenantId, message));
        return new ValueTask();
    }
}
