using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.AsyncDaemon.Testing.Bugs;

public class Bug_1995_empty_batch_update_failure : BugIntegrationContext
{
    [Fact]
    public async Task should_be_able_to_apply_an_update_batch_with_no_changes()
    {
        using var documentStore = SeparateStore(x =>
        {
            x.Events.StreamIdentity = StreamIdentity.AsGuid;
            x.Projections.Add(new IssueAggregateProjection(), ProjectionLifecycle.Async, "IssueAggregate");
        });

        await RunTest(documentStore);
    }

    private static async Task RunTest(IDocumentStore documentStore)
    {
        await documentStore.Advanced.Clean.CompletelyRemoveAllAsync();

        await using (var session = await documentStore.LightweightSessionAsync())
        {
            for (var i = 0; i < 499; i++)
            {
                var id = Guid.NewGuid();
                session.Events.StartStream(id, new IssueCreated { Id = id });
            }

            await session.SaveChangesAsync();
        }

        using var daemon = await documentStore.BuildProjectionDaemonAsync();
        await daemon.RebuildProjection<IssueAggregate>(default);
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
    public void Apply(IDocumentOperations operations, IReadOnlyList<StreamAction> streams)
    {
        var events = streams
            .SelectMany(x => x.Events)
            .OrderBy(x => x.Sequence)
            .Select(x => x.Data)
            .OfType<IssueCreated>()
            .ToList();

        operations.Store(events.Select(x => new IssueAggregate { Id = x.Id }));
    }

    public Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams, CancellationToken cancellation)
    {
        Apply(operations, streams);
        return Task.CompletedTask;
    }
}
