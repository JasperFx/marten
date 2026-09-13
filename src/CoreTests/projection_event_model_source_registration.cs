using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.EventModeling;
using JasperFx.Events.Projections;
using Marten;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

    private async Task<IHost> StartNamedHostAsync(string schemaSuffix, string? eventModelName,
        string declaredModelName, string declaredSliceName)
    {
        return await Host.CreateDefaultBuilder().ConfigureServices(services =>
        {
            services.AddMarten(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
                opts.DisableNpgsqlLogging = true;
                opts.DatabaseSchemaName = $"{SchemaName}_{schemaSuffix}";
                opts.Projections.Snapshot<SignalTally>(SnapshotLifecycle.Inline);

                // #5405: on StoreOptions rather than an AddMarten argument, so naming a model never
                // changes a public signature.
                opts.EventModelName = eventModelName;
            });

            // Deliberately AFTER AddMarten, which used to be the ordering that made this impossible.
            // The name is read off the resolved store when the model is assembled, not when the
            // store is registered, so registration order no longer matters.
            services.AddEventModel(declaredModelName,
                model => model.Slice(declaredSliceName).InDomain("Finance"));
        }).StartAsync();
    }

    /// <summary>
    /// #5405. A host that names its own Event Model gets ONE model, not two.
    /// </summary>
    /// <remarks>
    /// Slices merge by MODEL name, so a store contributing under the default name while the
    /// application called its model something else produces two assembled models that never meet.
    /// The symptom is not a missing slice -- it is "expected exactly one assembled model", which
    /// names neither Marten nor the line that caused it.
    /// </remarks>
    [Fact]
    public async Task a_named_store_lands_on_the_hosts_model_rather_than_a_second_one()
    {
        using var host = await StartNamedHostAsync("named", "Ledgers", "Ledgers", nameof(SignalTally));

        var models = await EventModelDiscovery.AssembleAsync(host.Services, CancellationToken.None);

        // One model. Without the name threaded through, this is two -- "Ledgers" and "EventModel".
        var model = models.ShouldHaveSingleItem();
        model.Name.ShouldBe("Ledgers");

        // And the store's derived slice is ON it, merged with the declared one rather than stranded
        // on a model of its own -- the half that a bare count of models would not catch.
        var slice = model.Slices.Single(x => x.Name == nameof(SignalTally));
        slice.Domain.ShouldBe("Finance");
        slice.ConsumedEvents.Select(x => x.Name).ShouldContain(nameof(SignalRaised));

        await host.StopAsync();
    }

    /// <summary>
    /// #5405, the discriminating half: a store nobody names still contributes to the DEFAULT model.
    /// </summary>
    /// <remarks>
    /// Asserting only that a named store lands on the named model would pass against a store that
    /// ignored the parameter entirely and put everything on whichever name the host declared. This
    /// is the behaviour that has to REMAIN for every host that never named a model.
    /// </remarks>
    [Fact]
    public async Task an_unnamed_store_still_contributes_to_the_default_model()
    {
        using var host = await StartNamedHostAsync("unnamed", null, "Ledgers", "SomethingDeclared");

        var models = await EventModelDiscovery.AssembleAsync(host.Services, CancellationToken.None);

        // Two models, on purpose: the host named its own, and the store was not told about it.
        models.Select(x => x.Name).OrderBy(x => x)
            .ShouldBe([ProjectionEventModelSource.DefaultModelName, "Ledgers"]);

        models.Single(x => x.Name == ProjectionEventModelSource.DefaultModelName)
            .Slices.Select(x => x.Name).ShouldContain(nameof(SignalTally));

        await host.StopAsync();
    }

    /// <summary>
    /// #5405. An empty or whitespace name is refused by the setter that takes it.
    /// </summary>
    /// <remarks>
    /// An empty string is a legal model name that reproduces the very bug this setting exists to
    /// prevent, with a blank where the name should be. Null is how you ask for the default model.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void a_blank_event_model_name_is_refused(string blank)
    {
        Should.Throw<ArgumentException>(() => new StoreOptions().EventModelName = blank)
            .ParamName.ShouldBe("value");
    }

    /// <summary>
    /// On the primary store the refusal lands before anything at all is registered.
    /// </summary>
    /// <remarks>
    /// <c>AddMarten(Action&lt;StoreOptions&gt;)</c> runs the configure callback eagerly, to build the
    /// StoreOptions it hands on, so a blank name throws out of the callback while the
    /// IServiceCollection is still untouched. That is what the emptiness assertion pins.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void a_blank_name_on_the_primary_store_is_refused_before_anything_is_registered(string blank)
    {
        var services = new ServiceCollection();

        Should.Throw<ArgumentException>(() => services.AddMarten(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.EventModelName = blank;
        }));

        services.ShouldBeEmpty();
    }

    /// <summary>
    /// An ancillary store refuses it too, but when the store is BUILT rather than at registration.
    /// </summary>
    /// <remarks>
    /// Both <c>AddMartenStore&lt;T&gt;</c> overloads wrap the caller's configuration in a
    /// <c>Func&lt;IServiceProvider, StoreOptions&gt;</c> that only runs when the store is first
    /// resolved, so there is no eager path here to refuse on — the guard fires on resolution
    /// instead. Asserting registration-time refusal for an ancillary store would be asserting
    /// something Marten does not do.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void a_blank_name_on_an_ancillary_store_is_refused_when_the_store_is_built(string blank)
    {
        var services = new ServiceCollection();

        // Registration itself is lazy, so this does NOT throw.
        services.AddMartenStore<IEventModelAncillaryStore>(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.EventModelName = blank;
        });

        using var provider = services.BuildServiceProvider();

        Should.Throw<ArgumentException>(() => provider.GetRequiredService<IEventModelAncillaryStore>());
    }

    /// <summary>
    /// Null is a legal value and means "the default model" — it must not trip the blank guard.
    /// </summary>
    [Fact]
    public void a_null_event_model_name_is_accepted_as_the_default()
    {
        var options = new StoreOptions { EventModelName = null };

        options.EventModelName.ShouldBeNull();
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
