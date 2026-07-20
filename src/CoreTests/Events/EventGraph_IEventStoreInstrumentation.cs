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

    // #4981: AddMarten always registers the SetEventStoreInstrumentation adapter, whose
    // Configure previously overwrote EnableExtendedProgressionTracking unconditionally -- so a
    // direct opt-in inside AddMarten was silently clobbered back to false at store build.
    [Fact]
    public void direct_toggle_inside_add_marten_survives_store_build()
    {
        var collection = new ServiceCollection();
        collection.AddMarten(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.Events.EnableExtendedProgressionTracking = true;
        });

        using var provider = collection.BuildServiceProvider();
        var store = provider.GetRequiredService<IDocumentStore>();

        store.Options.Events.EnableExtendedProgressionTracking.ShouldBeTrue();
    }

    // #4981: the adapter must apply, not clobber -- a direct opt-in and the DI-singleton opt-in
    // (what CritterWatch uses) compose rather than fighting.
    [Fact]
    public void direct_toggle_and_di_singleton_toggle_compose()
    {
        var collection = new ServiceCollection();
        collection.AddMarten(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.Events.EnableExtendedProgressionTracking = true;
        });

        using var provider = collection.BuildServiceProvider();

        // DI singleton left at its default (false) -- the direct toggle must still win.
        provider.GetRequiredService<IEventStoreInstrumentation>().ExtendedProgressionEnabled.ShouldBeFalse();

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
