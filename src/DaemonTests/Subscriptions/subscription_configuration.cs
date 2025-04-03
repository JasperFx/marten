using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Internals;
using Marten.Events.Projections;
using Marten.Services;
using Marten.Subscriptions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DaemonTests.Subscriptions;

public class subscription_configuration : OneOffConfigurationsContext
{
    [Fact]
    public void register_subscription_and_part_of_shards()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Subscribe(new FakeSubscription());
        });

        theStore.Options.Projections.AllShards().Select(x => x.Name.Identity)
            .ShouldContain("Fake:All");

    }

    [Fact]
    public async Task start_up_the_subscription()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Subscribe(new FakeSubscription());
        });

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAgentAsync("Fake:All", CancellationToken.None);
    }

    [Fact]
    public void validate_on_uniqueness_of_shard_names_with_subscriptions_and_projections()
    {
        Should.Throw<DuplicateSubscriptionNamesException>(() =>
        {
            StoreOptions(opts =>
            {
                opts.Projections.Subscribe(new FakeSubscription());
                opts.Projections.Add(new FakeProjection(), ProjectionLifecycle.Async, projectionName: "Fake");
            });
        });
    }
}

public class FakeProjection: IProjection
{
    public Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<IEvent> events, CancellationToken cancellation)
    {
        throw new System.NotImplementedException();
    }
}

public class FakeSubscription: SubscriptionBase
{
    public FakeSubscription()
    {
        SubscriptionName = "Fake";
    }

    public List<IEvent> EventsEncountered { get; } = new List<IEvent>();

    public override Task<IChangeListener> ProcessEventsAsync(EventRange page, ISubscriptionController controller,
        IDocumentOperations operations, CancellationToken cancellationToken)
    {
        EventsEncountered.AddRange(page.Events);
        return Task.FromResult((IChangeListener)Listener);
    }

    public FakeChangeListener Listener { get; } = new();
}

public class FakeChangeListener: IChangeListener
{
    public Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
    {
        AfterCommitWasCalled = true;
        return Task.CompletedTask;
    }

    public bool AfterCommitWasCalled { get; set; }

    public Task BeforeCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
    {
        BeforeCommitWasCalled = true;
        return Task.CompletedTask;
    }

    public bool BeforeCommitWasCalled { get; set; }
}
