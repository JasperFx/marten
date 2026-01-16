using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Grouping;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace EventSourcingTests.Aggregation;

public class when_enriching_events_for_aggregation_projections : OneOffConfigurationsContext
{
    [Fact]
    public async Task end_to_end()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add<UserTaskProjection>(ProjectionLifecycle.Async);
        });

        var user1 = new User { FirstName = "Patrick", LastName = "Mahomes" };
        var user2 = new User { FirstName = "Andy", LastName = "Reid" };
        var user3 = new User { FirstName = "Chris", LastName = "Jones" };
        var user4 = new User { FirstName = "Travis", LastName = "Kelce" };
        var user5 = new User { FirstName = "Xavier", LastName = "Worthy" };

        theSession.Store(user1, user2, user3, user4, user5);
        await theSession.SaveChangesAsync();

        theSession.Events.StartStream<UserTask>(new TaskStarted(), new UserAssigned { UserId = user1.Id }, new TaskStarted());
        theSession.Events.StartStream<UserTask>(new TaskStarted(), new UserAssigned { UserId = user2.Id }, new TaskStarted());
        theSession.Events.StartStream<UserTask>(new TaskStarted(), new UserAssigned { UserId = user3.Id }, new TaskStarted());
        theSession.Events.StartStream<UserTask>(new TaskStarted(), new UserAssigned { UserId = user4.Id }, new TaskStarted());
        theSession.Events.StartStream<UserTask>(new TaskStarted(), new UserAssigned { UserId = user5.Id }, new TaskStarted());
        theSession.Events.StartStream<UserTask>(new TaskStarted(), new UserAssigned { UserId = user2.Id }, new TaskStarted());
        theSession.Events.StartStream<UserTask>(new TaskStarted(), new UserAssigned { UserId = user3.Id }, new TaskStarted());
        theSession.Events.StartStream<UserTask>(new TaskStarted(), new UserAssigned { UserId = user5.Id }, new TaskStarted());
        theSession.Events.StartStream<UserTask>(new TaskStarted(), new UserAssigned { UserId = user1.Id }, new TaskStarted());
        theSession.Events.StartStream<UserTask>(new TaskStarted(), new UserAssigned { UserId = user1.Id }, new TaskStarted());
        theSession.Events.StartStream<UserTask>(new TaskStarted(), new UserAssigned { UserId = user3.Id }, new TaskStarted());
        theSession.Events.StartStream<UserTask>(new TaskStarted(), new UserAssigned { UserId = user2.Id }, new TaskStarted());
        await theSession.SaveChangesAsync();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(10.Seconds());
        await daemon.StopAllAsync();



    }
}

public class UserTask
{
    public Guid Id { get; set; }
    public bool HasStarted { get; set; }
    public bool HasCompleted { get; set; }
    public Guid? UserId { get; set; }

    // This would be sourced from the User
    // documents
    public string UserFullName { get; set; }
}

public record TaskLogged(string Name);
public record TaskStarted;
public record TaskFinished;

public class UserAssigned
{
    public Guid UserId { get; set; }

    // You don't *have* to do this with a mutable
    // property, but it is *an* easy way to pull this off
    public User? User { get; set; }
}

#region snippet_UserTaskProjection

public class UserTaskProjection: SingleStreamProjection<UserTask, Guid>
{
    // This is where you have a hook to "enrich" event data *after* slicing,
    // but before processing
    public override async Task EnrichEventsAsync(
        SliceGroup<UserTask, Guid> group,
        IQuerySession querySession,
        CancellationToken cancellation)
    {
        // First, let's find all the events that need a little bit of data lookup
        var assigned = group
            .Slices
            .SelectMany(x => x.Events().OfType<IEvent<UserAssigned>>())
            .ToArray();

        // Don't bother doing anything else if there are no matching events
        if (!assigned.Any()) return;

        var userIds = assigned.Select(x => x.Data.UserId)
            // Hey, watch this. Marten is going to helpfully sort this out for you anyway
            // but we're still going to make it a touch easier on PostgreSQL by
            // weeding out multiple ids
            .Distinct().ToArray();
        var users = await querySession.LoadManyAsync<User>(cancellation, userIds);

        // Just a convenience
        var lookups = users.ToDictionary(x => x.Id);
        foreach (var e in assigned)
        {
            if (lookups.TryGetValue(e.Data.UserId, out var user))
            {
                e.Data.User = user;
            }
        }
    }

    // This is the Marten 8 way of just writing explicit code in your projection
    public override UserTask Evolve(UserTask snapshot, Guid id, IEvent e)
    {
        snapshot ??= new UserTask { Id = id };
        switch (e.Data)
        {
            case UserAssigned assigned:
                snapshot.UserId = assigned?.User.Id;
                snapshot.UserFullName = assigned?.User.FullName;
                break;

            case TaskStarted:
                snapshot.HasStarted = true;
                break;

            case TaskFinished:
                snapshot.HasCompleted = true;
                break;
        }

        return snapshot;
    }
}

#endregion


