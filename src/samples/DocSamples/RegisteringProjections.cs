using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace DocSamples;

public class RegisteringProjections
{
    public static async Task register()
    {
        #region sample_registering_projections_with_different_lifecycles

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMarten(opts =>
        {
            opts.Connection(builder.Configuration.GetConnectionString("marten"));

            // Just in case you need Marten to "know" about a projection that will
            // only be calculated "Live", you can register it upfront
            opts.Projections.Add<MySpecialProjection>(ProjectionLifecycle.Live);

            // Or instead, we want strong consistency at all times
            // so that the stored projection documents always exactly reflect
            // the
            opts.Projections.Add<MySpecialProjection>(ProjectionLifecycle.Inline);

            // Or even differently, we can live with eventual consistency and
            // let Marten use its "Async Daemon" to continuously update the stored
            // documents being built out by our projection in the background
            opts.Projections.Add<MySpecialProjection>(ProjectionLifecycle.Async);

            // Just for the sake of completeness, "self-aggregating" types
            // can be registered as projections in Marten with this syntax
            // where "Snapshot" now means "a version of the projection from the events"
            opts.Projections.Snapshot<QuestParty>(SnapshotLifecycle.Inline);
            opts.Projections.Snapshot<QuestParty>(SnapshotLifecycle.Async);

            // This is the equivalent of ProjectionLifecycle.Live
            opts.Projections.LiveStreamAggregation<QuestParty>();
        });

        #endregion
    }

    public static async Task register2()
    {
        #region sample_registering_snapshots

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMarten(opts =>
        {
            opts.Connection(builder.Configuration.GetConnectionString("marten"));

            // Just for the sake of completeness, "self-aggregating" types
            // can be registered as projections in Marten with this syntax
            // where "Snapshot" now means "a version of the projection from the events"
            opts.Projections.Snapshot<QuestParty>(SnapshotLifecycle.Inline);
            opts.Projections.Snapshot<QuestParty>(SnapshotLifecycle.Async);

            // This is the equivalent of ProjectionLifecycle.Live
            // This is no longer necessary with Marten 8, but may be necessary
            // for *future* optimizations
            opts.Projections.LiveStreamAggregation<QuestParty>();
        });

        #endregion
    }

}

public class MySpecialProjection: EventProjection
{
    public override ValueTask ApplyAsync(IDocumentOperations operations, IEvent e, CancellationToken cancellation)
    {
        // Do whatever this projection does here...
        return base.ApplyAsync(operations, e, cancellation);
    }
}
