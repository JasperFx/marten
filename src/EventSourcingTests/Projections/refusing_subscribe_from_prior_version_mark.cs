using System;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Projections;

/// <summary>
/// #6186: a new Version of an aggregate projection writes to a new, empty table, and Marten runs no projection
/// backfills (#5632). Started from the prior version's mark, it would build every document from the events after
/// the mark only, so that combination is refused when the store is built. Everything around it is left alone.
/// </summary>
public class refusing_subscribe_from_prior_version_mark
{
    private static InvalidProjectionException refused(Action<StoreOptions> configure) =>
        Should.Throw<InvalidProjectionException>(() => buildStore(configure));

    private static void accepted(Action<StoreOptions> configure)
    {
        using var store = buildStore(configure);
    }

    private static DocumentStore buildStore(Action<StoreOptions> configure) => DocumentStore.For(opts =>
    {
        opts.Connection(ConnectionSource.ConnectionString);
        configure(opts);
    });

    [Fact]
    public void a_version_bumped_single_stream_projection_is_refused()
    {
        var ex = refused(opts =>
            opts.Projections.Add(new PriorMarkTallyProjection(2, true), ProjectionLifecycle.Async));

        ex.Message.ShouldContain(typeof(PriorMarkTally).FullName!.Replace('+', '.'));
        ex.Message.ShouldContain("Version 2");
        ex.Message.ShouldContain("SubscribeFromPriorVersionMark()");
        ex.Message.ShouldContain("https://github.com/JasperFx/marten/issues/5632");
    }

    [Fact]
    public void a_version_bumped_multi_stream_projection_is_refused()
    {
        var ex = refused(opts =>
            opts.Projections.Add(new PriorMarkTotalProjection(), ProjectionLifecycle.Async));

        ex.Message.ShouldContain(typeof(PriorMarkTotal).FullName!.Replace('+', '.'));
    }

    [Fact]
    public void a_version_bumped_composite_projecting_an_aggregate_is_refused()
    {
        var ex = refused(opts => opts.Projections.CompositeProjectionFor("PriorMarkComposite", c =>
        {
            c.Version = 2;
            c.Options.SubscribeFromPriorVersionMark("tallies-v2");
            c.Add(new PriorMarkTallyProjection(1, false));
        }));

        ex.Message.ShouldContain("Composite projection 'PriorMarkComposite'");
        ex.Message.ShouldContain(typeof(PriorMarkTally).FullName!.Replace('+', '.'));
    }

    [Fact]
    public void a_version_bumped_aggregate_without_the_mark_is_accepted()
    {
        accepted(opts => opts.Projections.Add(new PriorMarkTallyProjection(2, false), ProjectionLifecycle.Async));
    }

    [Fact]
    public void the_first_version_of_an_aggregate_with_the_mark_is_accepted()
    {
        // With no prior version, the mark is the projection's own progress: an ordinary resume
        accepted(opts => opts.Projections.Add(new PriorMarkTallyProjection(1, true), ProjectionLifecycle.Async));
    }

    [Fact]
    public void an_inline_aggregate_with_the_mark_is_accepted()
    {
        // The daemon never starts an inline projection, so the starting position is never read
        accepted(opts => opts.Projections.Add(new PriorMarkTallyProjection(2, true), ProjectionLifecycle.Inline));
    }

    [Fact]
    public void a_version_bumped_event_projection_with_the_mark_is_accepted()
    {
        // The documented use: a new version that works on the documents the prior version wrote
        accepted(opts => opts.Projections.Add(new PriorMarkEventProjection(), ProjectionLifecycle.Async));
    }
}

public record PriorMarkCounted(Guid TotalId);

public class PriorMarkTally
{
    public Guid Id { get; set; }
    public int Count { get; set; }
}

public partial class PriorMarkTallyProjection: SingleStreamProjection<PriorMarkTally, Guid>
{
    public PriorMarkTallyProjection(uint version, bool subscribeFromPriorVersionMark)
    {
        Version = version;
        if (subscribeFromPriorVersionMark) Options.SubscribeFromPriorVersionMark("tallies-v2");
    }

    public void Apply(PriorMarkTally tally, PriorMarkCounted _) => tally.Count++;
}

public class PriorMarkTotal
{
    public Guid Id { get; set; }
    public int Count { get; set; }
}

public partial class PriorMarkTotalProjection: MultiStreamProjection<PriorMarkTotal, Guid>
{
    public PriorMarkTotalProjection()
    {
        Version = 3;
        Options.SubscribeFromPriorVersionMark("totals-v3");
        Identity<PriorMarkCounted>(x => x.TotalId);
    }

    public void Apply(PriorMarkTotal total, PriorMarkCounted _) => total.Count++;
}

public partial class PriorMarkEventProjection: EventProjection
{
    public PriorMarkEventProjection()
    {
        Version = 2;
        Options.SubscribeFromPriorVersionMark("counts-v2");
    }

    public PriorMarkTally Create(IEvent<PriorMarkCounted> e) => new() { Id = e.Data.TotalId, Count = 1 };
}
