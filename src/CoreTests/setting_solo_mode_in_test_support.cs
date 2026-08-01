using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Events.Daemon;
using Marten;
using Marten.Events.Daemon.Coordination;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace CoreTests;

public class setting_solo_mode_in_test_support: HostedStoreContext
{
    [Fact]
    public async Task override_every_store_to_use_a_solo_async_daemon()
    {
        // Mostly just to prove we can mix and match daemon registrations
        var host = await StartHostAsync(_ => { }, DaemonMode.HotCold,
            configureServices: services =>
            {
                services.AddMartenStore<IFirstStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = $"{SchemaName}_first";
                }).AddAsyncDaemon(DaemonMode.HotCold);

                services.AddMartenStore<ISecondStore>(s =>
                {
                    var opts = new StoreOptions();
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = $"{SchemaName}_second";

                    return opts;
                }).AddAsyncDaemon(DaemonMode.HotCold);

                // Forget what the application says, let's make all the daemons run in solo mode!
                services.MartenDaemonModeIsSolo();
            });

        // 9.0: JFx.Events 2.0 introduced its own IProjectionCoordinator(<T>); qualify
        // with the full Marten namespace path to disambiguate the resolution this test
        // asserts on.
        host.Services.GetRequiredService<Marten.Events.Daemon.Coordination.IProjectionCoordinator>().ShouldBeOfType<ExplicitProjectionCoordinator>();
        host.Services.GetRequiredService<Marten.Events.Daemon.Coordination.IProjectionCoordinator<IFirstStore>>()
            .ShouldBeOfType<ExplicitProjectionCoordinator<IFirstStore>>();
        host.Services.GetRequiredService<Marten.Events.Daemon.Coordination.IProjectionCoordinator<ISecondStore>>()
            .ShouldBeOfType<ExplicitProjectionCoordinator<ISecondStore>>();
    }
}
