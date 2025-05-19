using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Xunit;

namespace DaemonTests.Bugs;

public class Bug_1995_empty_batch_update_failure : BugIntegrationContext
{
    [Fact]
    public async Task should_be_able_to_apply_an_update_batch_with_no_changes()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsGuid;
            opts.Projections.Add(new IssueAggregateProjection(), ProjectionLifecycle.Async, "IssueAggregate");
        }, true);

        for (var i = 0; i < 499; i++)
        {
            var id = Guid.NewGuid();
            theSession.Events.StartStream(id, new IssueCreated { Id = id });
        }

        await theSession.SaveChangesAsync();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.RebuildProjectionAsync<IssueAggregateProjection>(default);
    }
}

public class IssueCreated
{
    public Guid Id { get; set; }
}

public class IssueAggregate
{
    public Guid Id { get; set; }
}

public class IssueAggregateProjection : IProjection
{
    public Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<IEvent> events, CancellationToken cancellation)
    {
        var createds = events
            .Select(x => x.Data)
            .OfType<IssueCreated>()
            .ToList();

        operations.Store(createds.Select(x => new IssueAggregate { Id = x.Id }));
        return Task.CompletedTask;
    }
}
