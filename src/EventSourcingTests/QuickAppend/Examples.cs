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
                // This is the default Marten behavior from 4.0 on
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
