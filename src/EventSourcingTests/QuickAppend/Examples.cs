using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Microsoft.Extensions.Hosting;

namespace EventSourcingTests.QuickAppend;

public class Examples
{
    public static async Task configure()
    {
        #region sample_configuring_event_append_mode

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMarten(opts =>
            {
                // "Rich" was the default behavior through Marten 8. As of Marten 9
                // the default is EventAppendMode.QuickWithServerTimestamps.
                opts.Events.AppendMode = EventAppendMode.Rich;

                // Lighter weight mode that should result in better
                // performance, but with a loss of available metadata
                // within inline projections
                opts.Events.AppendMode = EventAppendMode.Quick;
            })
            .UseNpgsqlDataSource();

        #endregion
    }
}
