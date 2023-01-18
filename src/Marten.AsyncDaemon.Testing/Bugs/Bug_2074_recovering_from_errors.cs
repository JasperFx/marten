using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Marten.AsyncDaemon.Testing.Bugs;

public class Bug_2074_recovering_from_errors
{
    [Fact]
    public async Task do_not_blow_up()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddConsole());
        services.AddMarten(options =>
        {
            options.Connection(ConnectionSource.ConnectionString);
            options.DatabaseSchemaName = "bug2074";

            options.Projections.Add<UserIssueCounterProjection>();

            options.Projections.OnApplyEventException()
                .RetryLater(250.Milliseconds(), 500.Milliseconds(), 1.Seconds())
                .Then
                .Pause(5.Seconds());
        });

        await using var provider = services.BuildServiceProvider();

        var documentStore = provider.GetRequiredService<IDocumentStore>();

        await documentStore.Advanced.Clean.CompletelyRemoveAllAsync();

        var logger = provider.GetRequiredService<ILogger<IProjectionDaemon>>();
        using var daemon = await documentStore.BuildProjectionDaemonAsync(logger:logger).ConfigureAwait(false);
        await daemon.StartAllShards();

        var waiter = daemon.Tracker.WaitForShardState("UserIssueCounter:All", 1000, 1.Hours());

        var id = Guid.NewGuid();
        var events = Enumerable.Range(0, 1000).Select(x => new IssueCountIncremented(id)).ToList();
        await using (var session = await documentStore.LightweightSessionAsync())
        {
            session.Events.Append(id, events);
            await session.SaveChangesAsync();
        }

        await waiter;
    }
}

public record IssueCountIncremented(Guid Id);

public class UserIssueCounter
{
    public Guid Id { get; set; }
    public int Count { get; set; }
}

public class UserIssueCounterProjection : MultiStreamAggregation<UserIssueCounter, Guid>
{
    private static int _attempts;

    public UserIssueCounterProjection()
    {
        Lifecycle = ProjectionLifecycle.Async;
        Identity<IssueCountIncremented>(x => x.Id);
    }

    public void Apply(UserIssueCounter state, IssueCountIncremented change)
    {
        _attempts++;
        if (_attempts <= 2)
        {
            throw new Exception("Something has gone terribly wrong");
        }

        state.Id = change.Id;
        state.Count++;
    }
}
