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

    // #4687: default flipped from false → true (Critter Stack 1.0 timing). The monitoring
    // columns are written from existing daemon runtime state so the cost is negligible, and
    // they're useful for any stuck-shard diagnosis -- not just CritterWatch.
    //
    // #5310: this guard is the whole reason that flip regressed unnoticed. 824e3b783 added the
    // `= true` and renamed this test to default_is_enabled; 769a6fd5e reverted both the next day
    // while moving the property onto the SetEventStoreInstrumentation adapters, leaving this
    // comment arguing for a default the test underneath it pinned as false. Both shipped in
    // V9.30.0. If this is ever flipped back, flip the comment with it.
    [Fact]
    public void default_is_enabled()
    {
        new StoreOptions().EventGraph.EnableExtendedProgressionTracking.ShouldBeTrue();
    }

    // #5310: the scope of the regression was wider than "a bare DocumentStore.For(...) gets false"
    // -- no path defaulted to true, AddMarten included. Pin the DI path separately, since it runs
    // the adapters and the bare StoreOptions constructor does not.
    [Fact]
    public void add_marten_alone_is_enabled()
    {
        var collection = new ServiceCollection();
        collection.AddMarten(ConnectionSource.ConnectionString);

        using var provider = collection.BuildServiceProvider();
        provider.GetRequiredService<IDocumentStore>()
            .Options.Events.EnableExtendedProgressionTracking.ShouldBeTrue();
    }

    // #5310: with a true default, an opt-out has to actually opt out. #4981's guard was
    // `if (ExtendedProgressionEnabled)`, which cannot fire for false -- correct while the default
    // was false, silently inert the moment it is true. The adapter now distinguishes unset from
    // explicitly-false, so this is the case that would regress if it ever goes back to a plain bool.
    [Fact]
    public void explicit_opt_out_on_the_di_singleton_is_honored()
    {
        var collection = new ServiceCollection();
        collection.AddMarten(ConnectionSource.ConnectionString);

        using var provider = collection.BuildServiceProvider();
        provider.GetRequiredService<IEventStoreInstrumentation>().ExtendedProgressionEnabled = false;

        provider.GetRequiredService<IDocumentStore>()
            .Options.Events.EnableExtendedProgressionTracking.ShouldBeFalse();
    }

    // #5310: and the direct opt-out has to survive the adapter too -- the mirror of
    // direct_toggle_inside_add_marten_survives_store_build, which only covered opting IN.
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

    // #4981: the adapter must apply, not clobber. Under the #5310 true default the interesting
    // direction is the opposite one from the original test -- an untouched DI singleton must leave
    // a direct opt-OUT alone, which is exactly what an unconditional assignment would break.
    [Fact]
    public void untouched_di_singleton_does_not_clobber_a_direct_toggle()
    {
        var collection = new ServiceCollection();
        collection.AddMarten(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.Events.EnableExtendedProgressionTracking = false;
        });

        using var provider = collection.BuildServiceProvider();

        // Never assigned, so the adapter has nothing to apply and the direct toggle stands. The
        // getter reports the EventGraph default it would leave in place rather than default(bool).
        provider.GetRequiredService<IEventStoreInstrumentation>().ExtendedProgressionEnabled.ShouldBeTrue();

        var store = provider.GetRequiredService<IDocumentStore>();
        store.Options.Events.EnableExtendedProgressionTracking.ShouldBeFalse();
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
