using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Core;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Projections;
using Marten.Schema;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace DaemonTests;

public class multi_stream_aggregation_end_to_end: DaemonContext
{
    public multi_stream_aggregation_end_to_end(ITestOutputHelper output): base(output)
    {
    }

    [Fact]
    public async Task Bug_1947_better_is_new_logic()
    {
        Guid user1 = Guid.NewGuid();
        Guid user2 = Guid.NewGuid();
        Guid user3 = Guid.NewGuid();

        Guid issue1 = Guid.NewGuid();
        Guid issue2 = Guid.NewGuid();
        Guid issue3 = Guid.NewGuid();

        StoreOptions(opts =>
        {
            opts.Projections.AsyncMode = DaemonMode.Solo;
            opts.Projections.Add<UserIssueProjection>(ProjectionLifecycle.Async);
        });


        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(user1, new UserCreated { UserId = user1 });
            session.Events.Append(user2, new UserCreated { UserId = user2 });
            session.Events.Append(user3, new UserCreated { UserId = user3 });

            await session.SaveChangesAsync();
        }

        using var daemon = await StartDaemon();
        await daemon.StartAllAsync();

        await daemon.Tracker.WaitForShardState("UserIssue:All", 3, 15.Seconds());


        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(issue1, new IssueCreated { UserId = user1, IssueId = issue1 });
            await session.SaveChangesAsync();
        }

        // We need to ensure that the events are not processed in a single slice to hit the IsNew issue on multiple
        // slices which is what causes the loss of information in the projection.
        await daemon.Tracker.WaitForShardState("UserIssue:All", 4, 15.Seconds());

        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(issue2, new IssueCreated { UserId = user1, IssueId = issue2 });
            await session.SaveChangesAsync();
        }

        await daemon.Tracker.WaitForShardState("UserIssue:All", 5, 15.Seconds());

        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(issue3, new IssueCreated { UserId = user1, IssueId = issue3 });
            await session.SaveChangesAsync();
        }

        await daemon.Tracker.WaitForShardState("UserIssue:All", 6, 15.Seconds());

        await using (var session = theStore.QuerySession())
        {
            var doc = await session.LoadAsync<UserIssues>(user1);
            doc.Issues.Count.ShouldBe(3);
        }
    }
}

public class UserIssues
{
    [Identity] public Guid UserId { get; set; }

    public List<Issue> Issues { get; set; } = new List<Issue>();
}

public class Issue
{
    public Guid Id { get; set; }
    public string Name { get; set; }
}

public class UserCreated: IUserEvent
{
    public Guid UserId { get; set; }
}

public class IssueCreated: IUserEvent
{
    public Guid UserId { get; set; }
    public Guid IssueId { get; set; }
    public string Name { get; set; }
}

public interface IUserEvent
{
    public Guid UserId { get; }
}

public class UserIssueProjection: MultiStreamProjection<UserIssues, Guid>
{
    public UserIssueProjection()
    {
        ProjectionName = "UserIssue";

        Identity<UserCreated>(x => x.UserId);
        Identity<IssueCreated>(x => x.UserId);
    }

    public UserIssues Create(UserCreated @event) =>
        new UserIssues { UserId = @event.UserId, Issues = new List<Issue>() };

    public void Apply(UserIssues state, IssueCreated @event) =>
        state.Issues.Add(new Issue { Id = @event.IssueId, Name = @event.Name });
}
