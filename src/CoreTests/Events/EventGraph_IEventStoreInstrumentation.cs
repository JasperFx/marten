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

    // The default is ON since 10.0 (#5763; it was flipped in 9.7.0, reverted in 9.7.1, and #5318 said to
    // wait for a major). Opt out with `opts.Events.EnableExtendedProgressionTracking = false`.
    [Fact]
    public void default_is_enabled()
    {
        new StoreOptions().EventGraph.EnableExtendedProgressionTracking.ShouldBeTrue();
    }

    // The DI path is pinned separately from the bare constructor because it runs the
    // SetEventStoreInstrumentation adapters and the constructor does not -- so "on by default" has to
    // be true of both, and an adapter that clobbers is exactly how that would stop being true.
    [Fact]
    public void add_marten_alone_is_enabled()
    {
        var collection = new ServiceCollection();
        collection.AddMarten(ConnectionSource.ConnectionString);

        using var provider = collection.BuildServiceProvider();
        provider.GetRequiredService<IDocumentStore>()
            .Options.Events.EnableExtendedProgressionTracking.ShouldBeTrue();
    }

    // The adapter distinguishes unset from explicitly-false, which matters now that the default is true
    // -- it is it is the difference between an opt-out that works and one that silently
    // does nothing, and #4981's `if (ExtendedProgressionEnabled)` could not tell those apart. Pinned so
    // that a future default change does not quietly break the opt-out the way it nearly did.
    [Fact]
    public void explicit_opt_out_on_the_di_singleton_is_honored()
    {
        var collection = new ServiceCollection();
        collection.AddMarten(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.Events.EnableExtendedProgressionTracking = true;
        });

        using var provider = collection.BuildServiceProvider();
        provider.GetRequiredService<IEventStoreInstrumentation>().ExtendedProgressionEnabled = false;

        provider.GetRequiredService<IDocumentStore>()
            .Options.Events.EnableExtendedProgressionTracking.ShouldBeFalse();
    }

    // The mirror of direct_toggle_inside_add_marten_survives_store_build, which only covered opting IN.
    [Fact]
    public void direct_opt_out_inside_add_marten_survives_store_build()
    {
        var collection = new ServiceCollection();
        collection.AddMarten(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.Events.EnableExtendedProgressionTracking = false;
        });

        using var provider = collection.BuildServiceProvider();
        provider.GetRequiredService<IDocumentStore>()
            .Options.Events.EnableExtendedProgressionTracking.ShouldBeFalse();
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

    // #4981: the adapter must apply, not clobber. An untouched DI singleton has to leave a direct
    // opt-IN alone, which is the case that unconditional assignment broke.
    [Fact]
    public void untouched_di_singleton_does_not_clobber_a_direct_toggle()
    {
        var collection = new ServiceCollection();
        collection.AddMarten(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.Events.EnableExtendedProgressionTracking = true;
        });

        using var provider = collection.BuildServiceProvider();

        // Never assigned, so the adapter has nothing to apply and the direct toggle stands. The getter
        // reports the EventGraph default it would leave in place.
        provider.GetRequiredService<IEventStoreInstrumentation>().ExtendedProgressionEnabled.ShouldBeTrue();

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
