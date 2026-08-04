using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Events.Aggregation;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DaemonTests.EventProjections;

/// <summary>
/// #5169 — <c>EventProjectionScenario</c>'s up-front wipe derived its list of document types from
/// <c>Options.StorageTypes</c>. A <c>CompositeProjection</c> never populates its own <c>StorageTypes</c>
/// (its members hold theirs), so for a store whose entire read side is one composite the wipe loop
/// iterated NOTHING: event data was deleted, the composite's read models were not, and every scenario
/// after the first ran against the previous scenario's documents — now orphaned from any events.
/// Silent, and the exact opposite of what the harness leads with.
/// </summary>
public class Bug_5169_composite_scenario_wipe: OneOffConfigurationsContext
{
    private void configure()
    {
        StoreOptions(opts =>
        {
            opts.Projections.CompositeProjectionFor("Wipe5169", composite =>
            {
                // Stage 1 snapshot, stage 2 read model — the reporter's shape.
                composite.Snapshot<Wipe5169Invoice>();
                composite.Add(new Wipe5169OverviewProjection(), 2);
            });
        });
    }

    [Fact]
    public void the_composite_reports_the_document_types_its_members_write()
    {
        configure();

        var published = theStore.Options.Projections.All.SelectMany(x => x.PublishedTypes()).ToArray();

        published.ShouldContain(typeof(Wipe5169Invoice));
        published.ShouldContain(typeof(Wipe5169Overview));

        // ...and why the wipe could not use StorageTypes: a composite holds none of its own. That list is
        // documented as a schema-building hint, and the members are where the real values live.
        theStore.Options.Projections.All.SelectMany(x => x.Options.StorageTypes).ShouldBeEmpty();
    }

    [Fact]
    public async Task a_second_scenario_starts_from_a_clean_slate()
    {
        configure();

        var first = Guid.NewGuid();

        await theStore.Advanced.EventProjectionScenario(scenario =>
        {
            scenario.StartStream<Wipe5169Invoice>(first, new Wipe5169Created(1_000_000m));
            scenario.DocumentShouldExist<Wipe5169Invoice>(first);
            scenario.DocumentShouldExist<Wipe5169Overview>(first);
        });

        // A second scenario touching nothing related. Before #5169 the first scenario's read models
        // survived the wipe while its events were deleted, so this saw both.
        var second = Guid.NewGuid();

        await theStore.Advanced.EventProjectionScenario(scenario =>
        {
            scenario.StartStream<Wipe5169Invoice>(second, new Wipe5169Created(1m));
            scenario.DocumentShouldExist<Wipe5169Invoice>(second);
            scenario.DocumentShouldExist<Wipe5169Overview>(second);
        });

        await using var query = theStore.QuerySession();

        // The leak the issue is about: an unfiltered Query<T>() inside AssertAgainstProjectedData used to
        // see every prior scenario's rows.
        (await query.Query<Wipe5169Overview>().CountAsync()).ShouldBe(1);
        (await query.Query<Wipe5169Invoice>().CountAsync()).ShouldBe(1);

        (await query.LoadAsync<Wipe5169Overview>(first)).ShouldBeNull();
        (await query.LoadAsync<Wipe5169Invoice>(first)).ShouldBeNull();
    }
}

public record Wipe5169Created(decimal Amount);

// Self-aggregating: CompositeProjection.Snapshot<T>() builds a SingleStreamProjection<T, TId> over it.
public class Wipe5169Invoice
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }

    public void Apply(Wipe5169Created e) => Amount = e.Amount;
}

public class Wipe5169Overview
{
    public Guid Id { get; set; }
    public decimal ClaimedAmount { get; set; }
}

public partial class Wipe5169OverviewProjection: SingleStreamProjection<Wipe5169Overview, Guid>
{
    public void Apply(Wipe5169Overview overview, Wipe5169Created e) => overview.ClaimedAmount = e.Amount;
}
