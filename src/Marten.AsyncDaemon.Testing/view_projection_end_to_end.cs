using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Baseline.Dates;
using Marten.AsyncDaemon.Testing.TestingSupport;
using Marten.Events.Daemon.Resiliency;
using Marten.Events.Projections;
using Marten.Schema;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.AsyncDaemon.Testing
{
    public class view_projection_end_to_end : DaemonContext
    {
        public view_projection_end_to_end(ITestOutputHelper output) : base(output)
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
                opts.Projections.Add<UserIssueProjection>();
            });


            using (var session = theStore.LightweightSession())
            {
                session.Events.Append(user1, new UserCreated {UserId = user1});
                session.Events.Append(user2, new UserCreated {UserId = user2});
                session.Events.Append(user3, new UserCreated {UserId = user3});

                await session.SaveChangesAsync();
            }

            using var daemon = await StartDaemon();
            await daemon.StartAllShards();

            await daemon.Tracker.WaitForShardState("UserIssue:All", 3, 15.Seconds());


            using (var session = theStore.LightweightSession())
            {
                session.Events.Append(issue1, new IssueCreated {UserId = user1, IssueId = issue1});
                await session.SaveChangesAsync();
            }

            using (var session = theStore.LightweightSession())
            {
                session.Events.Append(issue2, new IssueCreated {UserId = user1, IssueId = issue2});
                await session.SaveChangesAsync();
            }

            using (var session = theStore.LightweightSession())
            {
                session.Events.Append(issue3, new IssueCreated {UserId = user1, IssueId = issue3});
                await session.SaveChangesAsync();
            }



            await daemon.Tracker.WaitForShardState("UserIssue:All", 6, 15.Seconds());

            using (var session = theStore.QuerySession())
            {
                var doc = await session.LoadAsync<UserIssues>(user1);
                doc.Issues.Count.ShouldBe(3);
            }
        }
    }

    public class UserIssues
    {
        [Identity]
        public Guid UserId { get; set; }

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

    public class UserIssueProjection: ViewProjection<UserIssues, Guid>
    {
        public UserIssueProjection()
        {
            ProjectionName = "UserIssue";

            Lifecycle = ProjectionLifecycle.Async;
            Identity<UserCreated>(x => x.UserId);
            Identity<IssueCreated>(x => x.UserId);
        }

        public UserIssues Create(UserCreated @event) =>
            new UserIssues {UserId = @event.UserId, Issues = new List<Issue>()};

        public void Apply(UserIssues state, IssueCreated @event) =>
            state.Issues.Add(new Issue {Id = @event.IssueId, Name = @event.Name});
    }
}
