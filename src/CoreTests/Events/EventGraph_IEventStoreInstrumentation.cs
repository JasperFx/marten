using System.Threading.Tasks;
using CoreTests.Examples;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace CoreTests.Events;

public class EventGraph_IEventStoreInstrumentation
{

    // #4687: default flipped from false → true (Critter Stack 1.0 timing). The six
    // monitoring columns are written from existing daemon runtime state so the cost is
    // negligible, and they're useful for any stuck-shard diagnosis -- not just CritterWatch.
    // Opt-out remains via either name.
    [Fact]
    public void default_is_disabled()
    {
        new StoreOptions().EventGraph.EnableExtendedProgressionTracking.ShouldBeFalse();
    }

    [Fact]
    public void build_store_with_progression_tracking_override()
    {
        var collection = new ServiceCollection();
        collection.AddMarten(ConnectionSource.ConnectionString);

        using var provider = collection.BuildServiceProvider();
        var instrumentation = provider.GetRequiredService<IEventStoreInstrumentation>();

        instrumentation.ShouldNotBeNull();
        instrumentation.ExtendedProgressionEnabled = true;

        var store = provider.GetRequiredService<IDocumentStore>();
        store.Options.Events.EnableExtendedProgressionTracking.ShouldBeTrue();
    }

    [Fact]
    public void build_store_with_progression_tracking_override_with_ancillary_store()
    {
        var collection = new ServiceCollection();
        collection.AddMarten(ConnectionSource.ConnectionString);
        collection.AddMartenStore<IInvoicingStore>(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "invoices";
        });

        using var provider = collection.BuildServiceProvider();

        var instruments = provider.GetServices<IEventStoreInstrumentation>();
        foreach (var instrument in instruments)
        {
            instrument.ExtendedProgressionEnabled = true;
        }

        var store = provider.GetRequiredService<IDocumentStore>();
        store.Options.Events.EnableExtendedProgressionTracking.ShouldBeTrue();

        provider.GetRequiredService<IInvoicingStore>()
            .Options.Events.EnableExtendedProgressionTracking.ShouldBeTrue();
    }
}
