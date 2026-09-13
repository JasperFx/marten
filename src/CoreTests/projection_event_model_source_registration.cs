using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.EventModeling;
using JasperFx.Events.Projections;
using Marten;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace CoreTests;

/*
 * #5394 (jasperfx#825). The store-derived Event Model rung: one SlicePattern.View slice per
 * registered projection -- the projection, the document it produces, and the events its Apply /
 * Create methods take -- read straight out of the store's own registry.
 *
 * Before this, nothing derived from the store. Bobcat declares slices, Wolverine derives Command and
 * Automation slices from its chains, CritterWatch observes a running system, and a View slice
 * appeared on a canvas only when a human had written one down -- grepping marten/src for
 * EventModelSource returned zero hits.
 *
 * These tests pin the REGISTRATION rather than the mapping. The mapping is JasperFx's and is tested
 * upstream against a substitute store; what only Marten can get wrong is whether AddMarten and
 * AddMartenStore<T> each hand ProjectionEventModelSource a resolver pointing at the right store --
 * which is exactly the thing a resolver-taking API exists to let a store get wrong.
 */
public interface IEventModelAncillaryStore: IDocumentStore;

public class projection_event_model_source_registration: HostedStoreContext
{
    private static async Task<EventModelSliceDescriptor[]> slicesFrom(IServiceProvider services)
    {
        var models = await EventModelDiscovery.AssembleAsync(services, CancellationToken.None);
        return models.SelectMany(x => x.Slices).ToArray();
    }

    [Fact]
    public async Task add_marten_derives_a_view_slice_for_a_registered_projection()
    {
        var host = await StartHostAsync(opts => opts.Projections.Snapshot<SignalTally>(SnapshotLifecycle.Inline));

        var slices = await slicesFrom(host.Services);

        var slice = slices.ShouldHaveSingleItem();

        // Named after the DOCUMENT, not the projection class: that is what makes a store-derived
        // slice merge by name with a Bobcat-declared one instead of showing up as a second sticky
        // saying the same thing.
        slice.Name.ShouldBe(nameof(SignalTally));
        slice.Pattern.ShouldBe(SlicePattern.View);
        slice.ReadModelTypes.Select(x => x.Name).ShouldContain(nameof(SignalTally));
        slice.ConsumedEvents.Select(x => x.Name).ShouldContain(nameof(SignalRaised));
    }

    /// <summary>
    /// The half that only a store can get wrong. An ancillary store registers under a marker type, so
    /// the resolver AddMartenStore&lt;T&gt; supplies has to name T -- a registration that reached for
    /// IDocumentStore instead would describe the primary store twice and never mention this one.
    /// </summary>
    [Fact]
    public async Task an_ancillary_store_derives_its_own_view_slices()
    {
        var host = await StartHostAsync(
            opts => opts.Projections.Snapshot<SignalTally>(SnapshotLifecycle.Inline),
            configureServices: services => services.AddMartenStore<IEventModelAncillaryStore>(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
                opts.DatabaseSchemaName = $"{SchemaName}_ancillary";
                opts.Projections.Snapshot<AncillaryTally>(SnapshotLifecycle.Inline);
            }));

        var slices = await slicesFrom(host.Services);

        slices.Select(x => x.Name)
            .ShouldContain(nameof(AncillaryTally),
                "The ancillary store contributed no View slice, so AddMartenStore<T> either did not register a source or registered one pointing at the primary store.");

        var ancillary = slices.Single(x => x.Name == nameof(AncillaryTally));
        ancillary.Pattern.ShouldBe(SlicePattern.View);
        ancillary.ConsumedEvents.Select(x => x.Name).ShouldContain(nameof(SignalLowered));

        // And the primary store is still described -- two stores in one host, two slices, neither
        // one swallowing the other.
        slices.Select(x => x.Name).ShouldContain(nameof(SignalTally));
    }
}

public record SignalRaised(string Flag);

public record SignalLowered(string Flag);

public class SignalTally
{
    public Guid Id { get; set; }
    public int Raised { get; set; }

    public void Apply(SignalRaised e) => Raised++;
}

public class AncillaryTally
{
    public Guid Id { get; set; }
    public int Lowered { get; set; }

    public void Apply(SignalLowered e) => Lowered++;
}
