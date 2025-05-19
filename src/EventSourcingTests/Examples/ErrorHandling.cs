using System.Threading.Tasks;
using JasperFx.Events.Daemon;
using Marten;
using Microsoft.Extensions.Hosting;

namespace EventSourcingTests.Examples;

public class ErrorHandling
{
    public static async Task bootstrapping_with_error_handling()
    {

        #region sample_async_daemon_error_policies

        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                    {
                        // connection information...


                        opts.Projections.Errors.SkipApplyErrors = true;
                        opts.Projections.Errors.SkipSerializationErrors = true;
                        opts.Projections.Errors.SkipUnknownEvents = true;

                        opts.Projections.RebuildErrors.SkipApplyErrors = false;
                        opts.Projections.RebuildErrors.SkipSerializationErrors = false;
                        opts.Projections.RebuildErrors.SkipUnknownEvents = false;
                    })
                    .AddAsyncDaemon(DaemonMode.HotCold);
            }).StartAsync();

        #endregion
    }
}
