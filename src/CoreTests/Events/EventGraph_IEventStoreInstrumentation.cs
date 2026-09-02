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

    // The default is OFF, deliberately and permanently, and this is the third time it has come up --
    // so the reasoning lives here rather than in a commit message.
    //
    // #4687 argued for flipping it to true, on the grounds that the columns are cheap because they
    // carry daemon runtime state that already exists. #5310 then found that the flip had landed in
    // 824e3b783 and been lost the next day in 769a6fd5e, and restored it as an unintended regression.
    //
    // That restoration was reversed. Turning this on by default is a BREAKING change: every existing
    // store that never asked for monitoring silently starts writing six columns on every shard state
    // transition after an upgrade. "It was originally meant to be true" is a reason to document the
    // opt-in well; it is not a reason to change what someone else's deployment does without them
    // asking. The cheapness argument is a fine argument for turning it ON and no argument at all for
    // turning it on for somebody else.
    //
    // If you are here because you are about to flip it again: don't, without a major version.
    [Fact]
    public void default_is_disabled()
    {
        new StoreOptions().EventGraph.EnableExtendedProgressionTracking.ShouldBeFalse();
    }

    // The DI path is pinned separately from the bare constructor because it runs the
    // SetEventStoreInstrumentation adapters and the constructor does not -- so "off by default" has to
    // be true of both, and an adapter that clobbers is exactly how that would stop being true.
    [Fact]
    public void add_marten_alone_is_disabled()
    {
        var collection = new ServiceCollection();
        collection.AddMarten(ConnectionSource.ConnectionString);

        using var provider = collection.BuildServiceProvider();
        provider.GetRequiredService<IDocumentStore>()
            .Options.Events.EnableExtendedProgressionTracking.ShouldBeFalse();
    }

    // The adapter distinguishes unset from explicitly-false, which is a no-op against today's false
    // default -- kept because it is the difference between an opt-out that works and one that silently
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
