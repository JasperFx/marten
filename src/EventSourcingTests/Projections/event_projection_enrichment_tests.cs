using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Projections;

public class event_projection_enrichment_tests : OneOffConfigurationsContext
{
    [Fact]
    public async Task enrichment_sets_data_before_apply_inline()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add(new SimpleEnrichmentProjection(), ProjectionLifecycle.Inline);
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // Use TaskAssigned as the only event — enrichment sets UserName,
        // ProjectAsync reads it and stores a TaskSummary
        var taskId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(taskId,
                new TaskAssigned { TaskId = taskId, UserId = Guid.NewGuid() });
            await session.SaveChangesAsync();
        }

        await using (var query = theStore.QuerySession())
        {
            var summary = await query.LoadAsync<TaskSummary>(taskId);
            summary.ShouldNotBeNull();
            // The enrichment hardcodes the name — if set, enrichment ran before Apply
            summary.AssignedUserName.ShouldBe("Enriched User");
        }
    }

    [Fact]
    public async Task enrichment_with_database_lookup_inline()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add(new DatabaseLookupEnrichmentProjection(), ProjectionLifecycle.Inline);
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // Pre-store a User
        var userId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Store(new User { Id = userId, FirstName = "Alice", LastName = "Smith" });
            await session.SaveChangesAsync();
        }

        var taskId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(taskId,
                new TaskAssigned { TaskId = taskId, UserId = userId });
            await session.SaveChangesAsync();
        }

        await using (var query = theStore.QuerySession())
        {
            var summary = await query.LoadAsync<TaskSummary>(taskId);
            summary.ShouldNotBeNull();
            summary.AssignedUserName.ShouldBe("Alice Smith");
        }
    }

    [Fact]
    public async Task enrichment_is_called_before_apply()
    {
        var callOrder = new List<string>();

        StoreOptions(opts =>
        {
            opts.Projections.Add(
                new CallOrderTrackingProjection(callOrder),
                ProjectionLifecycle.Inline);
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var streamId = Guid.NewGuid();
        await using var session = theStore.LightweightSession();
        session.Events.StartStream(streamId, new TaskCreated { TaskId = streamId, Title = "Test" });
        await session.SaveChangesAsync();

        callOrder.ShouldBe(new[] { "EnrichEventsAsync", "Apply:TaskCreated" });
    }
}

#region Test Events

public class TaskCreated
{
    public Guid TaskId { get; set; }
    public string Title { get; set; } = "";
}

public class TaskAssigned
{
    public Guid TaskId { get; set; }
    public Guid UserId { get; set; }
    public string? UserName { get; set; }
}

#endregion

#region Test Documents

public class TaskSummary
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string? AssignedUserName { get; set; }
}

#endregion

#region Simple Enrichment (no DB lookup)

public class SimpleEnrichmentProjection : EventProjection
{
    public SimpleEnrichmentProjection()
    {
        // TaskAssigned handler reads UserName that was set by EnrichEventsAsync
        Project<TaskAssigned>((e, ops) =>
        {
            ops.Store(new TaskSummary
            {
                Id = e.TaskId,
                AssignedUserName = e.UserName
            });
        });
    }

    public override Task EnrichEventsAsync(IQuerySession querySession,
        IReadOnlyList<IEvent> events, CancellationToken cancellation)
    {
        foreach (var e in events.OfType<IEvent<TaskAssigned>>())
        {
            e.Data.UserName = "Enriched User";
        }
        return Task.CompletedTask;
    }
}

#endregion

#region Database Lookup Enrichment

public class DatabaseLookupEnrichmentProjection : EventProjection
{
    public DatabaseLookupEnrichmentProjection()
    {
        // Stores a TaskSummary using the enriched UserName
        Project<TaskAssigned>((e, ops) =>
        {
            ops.Store(new TaskSummary
            {
                Id = e.TaskId,
                AssignedUserName = e.UserName
            });
        });
    }

    public override async Task EnrichEventsAsync(IQuerySession querySession,
        IReadOnlyList<IEvent> events, CancellationToken cancellation)
    {
        var assigned = events.OfType<IEvent<TaskAssigned>>().ToArray();
        if (assigned.Length == 0) return;

        var userIds = assigned.Select(e => e.Data.UserId).Distinct().ToArray();
        var users = await querySession.LoadManyAsync<User>(cancellation, userIds);
        var lookup = users.ToDictionary(u => u.Id);

        foreach (var e in assigned)
        {
            if (lookup.TryGetValue(e.Data.UserId, out var user))
            {
                e.Data.UserName = $"{user.FirstName} {user.LastName}";
            }
        }
    }
}

#endregion

#region Call Order Tracking

public class CallOrderTrackingProjection : EventProjection
{
    private readonly List<string> _callOrder;

    public CallOrderTrackingProjection(List<string> callOrder)
    {
        _callOrder = callOrder;

        Project<TaskCreated>((e, ops) =>
        {
            _callOrder.Add($"Apply:{nameof(TaskCreated)}");
        });
    }

    public override Task EnrichEventsAsync(IQuerySession querySession,
        IReadOnlyList<IEvent> events, CancellationToken cancellation)
    {
        _callOrder.Add(nameof(EnrichEventsAsync));
        return Task.CompletedTask;
    }
}

#endregion
