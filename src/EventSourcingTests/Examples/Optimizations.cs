using System.Threading.Tasks;
using Marten;
using Marten.Events;
using Microsoft.Extensions.Hosting;

namespace EventSourcingTests.Examples;

public class Optimizations
{
    public static async Task use_partitioning()
    {

        #region sample_turn_on_stream_archival_partitioning

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMarten(opts =>
        {
            opts.Connection("some connection string");

            // Turn on the PostgreSQL table partitioning for
            // hot/cold storage on archived events
            opts.Events.UseArchivedStreamPartitioning = true;
        });

        #endregion
    }

    public static async Task use_optimizations()
    {

        #region sample_turn_on_optimizations_for_event_sourcing

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMarten(opts =>
        {
            opts.Connection("some connection string");

            // Turn on the PostgreSQL table partitioning for
            // hot/cold storage on archived events
            opts.Events.UseArchivedStreamPartitioning = true;

            // Use the *much* faster workflow for appending events
            // at the cost of *some* loss of metadata usage for
            // inline projections
            opts.Events.AppendMode = EventAppendMode.Quick;

            // Little more involved, but this can reduce the number
            // of database queries necessary to process inline projections
            // during command handling with some significant
            // caveats
            opts.Events.UseIdentityMapForInlineAggregates = true;


            // Opts into a mode where Marten is able to rebuild single // [!code ++]
            // stream projections faster by building one stream at a time // [!code ++]
            // Does require new table migrations for Marten 7 users though // [!code ++]
            opts.Events.UseOptimizedProjectionRebuilds = true; // [!code ++]
        });

        #endregion
    }

}
