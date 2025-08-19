using System.Threading.Tasks;
using JasperFx.Events.Daemon;
using Marten;
using Marten.Events.Daemon.Coordination;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace CoreTests;

public class setting_solo_mode_in_test_support
{
    [Fact]
    public async Task override_every_store_to_use_a_solo_async_daemon()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                // Mostly just to prove we can mix and match
                services.AddMarten(ConnectionSource.ConnectionString).AddAsyncDaemon(DaemonMode.HotCold);

                services.AddMartenStore<IFirstStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "first_store";
                }).AddAsyncDaemon(DaemonMode.HotCold);

                services.AddMartenStore<ISecondStore>(services =>
                {
                    var opts = new StoreOptions();
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "second_store";

                    return opts;
                }).AddAsyncDaemon(DaemonMode.HotCold);

                // Forget what the application says, let's make all the daemons run in solo mode!
                services.MartenDaemonModeIsSolo();
            }).StartAsync();

        host.Services.GetRequiredService<IProjectionCoordinator>().Mode.ShouldBe(DaemonMode.Solo);
        host.Services.GetRequiredService<IProjectionCoordinator<IFirstStore>>().Mode.ShouldBe(DaemonMode.Solo);
        host.Services.GetRequiredService<IProjectionCoordinator<ISecondStore>>().Mode.ShouldBe(DaemonMode.Solo);
    }
}
