using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.CodeGeneration;
using JasperFx.Events;
using JasperFx.Events.Grouping;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DaemonTests.Bugs;

public class Bug_3162_daemon_should_always_check_highwater_on_rebuild : BugIntegrationContext
{
    private readonly Guid teamId = Guid.NewGuid();
    private readonly string teamName = "Yummy";

    private readonly Guid membershipId = Guid.NewGuid();
    private readonly Guid userId1 = Guid.NewGuid();
    private readonly Guid userId2 = Guid.NewGuid();

    [Fact]
    public async Task MethodName_ShouldDoWhat_WhenWhat()
    {
        StoreOptions(options =>
        {
            options.Projections.Snapshot<Team>(ProjectionLifecycle.Inline);
            options.Projections.Add<InvitationProjection>(ProjectionLifecycle.Async);
            options.GeneratedCodeMode = TypeLoadMode.Auto;
        });

        await using var session = theSession;

        session.Events.Append(teamId,
            new TeamCreated(teamId),
            new TeamNameChanged(teamName));

        await session.SaveChangesAsync();

        session.Events.Append(membershipId,
            new MemberInvited(membershipId,
                teamId,
                userId1,
                userId2));

        await session.SaveChangesAsync();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.RebuildProjectionAsync<InvitationProjection>(CancellationToken.None);

        await AssertInvitation(session, membershipId, teamName);

        session.Events.Append(membershipId,
            new MemberJoined());

        await session.SaveChangesAsync();

        await daemon.RebuildProjectionAsync<InvitationProjection>(CancellationToken.None);
        await AssertInvitationDoesNotExist(session, membershipId);

        return;

        async Task AssertInvitation(IDocumentSession assertSession, Guid assertMembershipId, string assertTeamName)
        {
            var invitationReadModel = await assertSession.Query<InvitationView>()
                .SingleOrDefaultAsync(p => p.Id == assertMembershipId);

            invitationReadModel.ShouldNotBeNull();
            invitationReadModel!.TeamName.ShouldBe(assertTeamName);
        }

        async Task AssertInvitationDoesNotExist(IDocumentSession assertSession, Guid assertMembershipId)
        {
            var invitation = await assertSession.Query<InvitationView>()
                .SingleOrDefaultAsync(p => p.Id == assertMembershipId);

            invitation.ShouldBeNull();
        }
    }
}

public record TeamCreated(Guid TeamId);
public record TeamNameChanged(string TeamName);

public sealed class Team
{
    public Guid Id { get; set; }
    public string TeamName { get; set; } = "";

    internal Team()
    { }

    public void Apply(TeamCreated @event)
    {
        Id = @event.TeamId;
    }

    public void Apply(TeamNameChanged @event)
    {
        TeamName = @event.TeamName;
    }
}

public record MemberInvited(Guid TeamMembershipId, Guid TeamId, Guid UserIdInviting, Guid UserIdInvited);
public record MemberJoined;

public sealed class InvitationView
{
    public Guid Id { get; set; }
    public Guid TeamId { get; set; }
    public Guid UserIdInviting { get; set; }
    public string TeamName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class TeamInvitationGrouper : IAggregateGrouper<Guid>
{
    private readonly Type[] eventTypes =
    [
        typeof(MemberInvited),
        typeof(MemberJoined),
    ];

    public async Task Group(
        IQuerySession session,
        IEnumerable<IEvent> events,
        IEventGrouping<Guid> grouping)
    {
        var eventsArray = events.ToArray();
        MembershipGroup(eventsArray, grouping);
        await TeamGroup(session, eventsArray, grouping);
    }

    private void MembershipGroup(
        IEnumerable<IEvent> events,
        IEventGrouping<Guid> grouping)
    {
        var filteredEvents = events
            .Where(ev => eventTypes.Contains(ev.EventType))
            .GroupBy(e => e.StreamId)
            .Select(x => new
            {
                TeamMembershipId = x.Key,
                Events = x.ToArray(),
            })
            .ToList();

        foreach (var membershipEvents in filteredEvents)
        {
            grouping.AddEvents(membershipEvents.TeamMembershipId, membershipEvents.Events);
        }
    }

    private async Task TeamGroup(
        IQuerySession session,
        IEnumerable<IEvent> events,
        IEventGrouping<Guid> grouping)
    {
        var teamEvents = events
            .OfType<IEvent<TeamNameChanged>>()
            .ToArray();

        if (teamEvents.Length == 0)
        {
            return;
        }

        var teamIds = teamEvents
            .Select(e => e.StreamId)
            .ToList();

        var result = await session.Query<InvitationView>()
            .Where(e => teamIds.Contains(e.TeamId))
            .Select(x => new { x.Id, x.TeamId })
            .ToListAsync();

        var eventsPerTeam = result.Select(g =>
            new
            {
                g.Id,
                Events = teamEvents.Where(ev => ev.StreamId == g.TeamId),
            });

        foreach (var group in eventsPerTeam)
        {
            grouping.AddEvents(group.Id, group.Events);
        }
    }
}

public sealed class InvitationProjection : MultiStreamProjection<InvitationView, Guid>
{
    public InvitationProjection()
    {
        IncludeType<TeamNameChanged>();
        IncludeType<MemberInvited>();
        IncludeType<MemberJoined>();

        CustomGrouping(new TeamInvitationGrouper());
    }

    public async Task<InvitationView> Create(
        IQuerySession session,
        IEvent<MemberInvited> @event)
    {
        var teamData = await session.Query<Team>()
            .Where(u => u.Id == @event.Data.TeamId)
            .Select(x => x.TeamName)
            .SingleOrDefaultAsync();

        return new InvitationView
        {
            Id = @event.Data.TeamMembershipId,
            TeamId = @event.Data.TeamId,
            UserIdInviting = @event.Data.UserIdInviting,
            TeamName = teamData ?? "",
            CreatedAt = @event.Timestamp,
        };
    }

    public InvitationView Apply(TeamNameChanged @event, InvitationView view)
    {
        view.TeamName = @event.TeamName;

        return view;
    }

    public bool ShouldDelete(MemberJoined @event)
    {
        return true;
    }
}

