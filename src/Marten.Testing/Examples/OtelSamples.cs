using System.Threading.Tasks;
using Marten.Services;
using Microsoft.Extensions.Hosting;

namespace Marten.Testing.Examples;

public class OtelSamples
{
    public static async Task opt_into_connection_tracking()
    {
        #region sample_enabling_normal_level_of_connection_tracking

        using var store = DocumentStore.For(opts =>
        {
            opts.Connection("some connection string");

            // Track Marten connection usage
            opts.OpenTelemetry.TrackConnections = TrackLevel.Normal;
        });

        #endregion
    }

    public static async Task opt_into_verbose_connection_tracking()
    {
        #region sample_enabling_verbose_level_of_connection_tracking

        using var store = DocumentStore.For(opts =>
        {
            opts.Connection("some connection string");

            // Track Marten connection usage *and* all the "write" operations
            // that Marten does with that connection
            opts.OpenTelemetry.TrackConnections = TrackLevel.Verbose;
        });

        #endregion
    }

    public static void opt_into_event_metrics()
    {
        #region sample_track_event_counters

        using var store = DocumentStore.For(opts =>
        {
            opts.Connection("some connection string");

            // Track the number of events being appended to the system
            opts.OpenTelemetry.TrackEventCounters();
        });

        #endregion
    }
}
