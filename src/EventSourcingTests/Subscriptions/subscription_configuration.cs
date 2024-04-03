using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten;
using Marten.Events;
using Marten.Events.Daemon.Internals;
using Marten.Subscriptions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Subscriptions;

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
}

public class FakeSubscription: SubscriptionBase
{
    public FakeSubscription()
    {
        SubscriptionName = "Fake";
    }

    public List<IEvent> EventsEncountered { get; } = new List<IEvent>();

    public override Task ProcessEventsAsync(EventRange page, IDocumentOperations operations, CancellationToken cancellationToken)
    {
        EventsEncountered.AddRange(page.Events);
        return Task.CompletedTask;
    }
}
