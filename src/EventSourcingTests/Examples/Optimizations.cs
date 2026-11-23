using System.Threading.Tasks;
using JasperFx.Events;
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

            // Opts into a mode where Marten is able to rebuild single
            // stream projections faster by building one stream at a time
            opts.Events.UseOptimizedProjectionRebuilds = true;
        });

        #endregion

        #region sample_turn_on_optimizations_for_rebuilding

        builder.Services.AddMarten(opts =>
        {
            opts.Connection("some connection string");

            // Opts into a mode where Marten is able to rebuild single
            // stream projections faster by building one stream at a time
            opts.Events.UseOptimizedProjectionRebuilds = true;
        });

        #endregion
    }

}
